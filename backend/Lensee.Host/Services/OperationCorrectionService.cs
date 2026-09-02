using System.Text.Json;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Services;

/// <summary>
/// Owns the maker/checker correction workflow for finalized operations.  Its
/// transaction includes the reversal, stock effect, financial settlement,
/// immutable audit record, and outbox envelope.
/// </summary>
public sealed class OperationCorrectionService
{
    private const string PendingApproval = "PendingApproval";
    private const string Approved = "Approved";
    private const string Rejected = "Rejected";
    private const string CashRefund = "CashRefund";
    private const string MerchantCredit = "MerchantCredit";

    private readonly OperationsDbContext _operations;
    private readonly InventoryDbContext _inventory;
    private readonly PaymentsDbContext _payments;
    private readonly IdentityDbContext _identity;
    private readonly SharedDbContext _shared;
    private readonly StockLedgerService _ledger;
    private readonly IClock _clock;

    public OperationCorrectionService(
        OperationsDbContext operations,
        InventoryDbContext inventory,
        PaymentsDbContext payments,
        IdentityDbContext identity,
        SharedDbContext shared,
        StockLedgerService ledger,
        IClock clock)
    {
        _operations = operations;
        _inventory = inventory;
        _payments = payments;
        _identity = identity;
        _shared = shared;
        _ledger = ledger;
        _clock = clock;
    }

    public async Task<CorrectionCommandResult> CreateAsync(
        Guid operationId,
        CreateOperationCorrectionCommand command,
        Guid requesterId,
        string? requesterRole,
        CancellationToken cancellationToken)
    {
        LenseeTelemetry.CorrectionRequests.Add(1, new KeyValuePair<string, object?>("operation", "create"));
        if (requesterId == Guid.Empty)
        {
            return CorrectionCommandResult.Forbidden();
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return CorrectionCommandResult.Validation("reason", "A correction reason is required.");
        }

        var settlementValidation = ValidateSettlement(command.SettlementMethod, command.SettlementAmount);
        if (settlementValidation is not null)
        {
            return settlementValidation;
        }

        var now = _clock.EgyptNow;
        CorrectionCommandResult? result = null;
        await SharedDbTransaction.ExecuteAsync(_operations, async () =>
        {
            // The source operation is the aggregate lock for correction creation.
            // Holding it before eligibility and pending-proposal checks makes the
            // partial unique index a last-line invariant rather than normal flow.
            var operation = await LoadOperationForUpdateAsync(operationId, cancellationToken);
            if (operation is null)
            {
                result = CorrectionCommandResult.NotFound();
                return;
            }
            if (!IsFinalized(operation.Status) || operation.RecordKind != "Standard")
            {
                result = CorrectionCommandResult.Conflict("Only a finalized original operation can enter the correction workflow.");
                return;
            }
            if (await _operations.OperationLogs.AnyAsync(value =>
                    value.ReversesOperationId == operation.Id && value.RecordKind == "Reversal" && !value.IsDeleted,
                    cancellationToken))
            {
                result = CorrectionCommandResult.Conflict("A reversal already exists for this operation.");
                return;
            }
            if (await _operations.OperationCorrectionProposals.AnyAsync(
                    value => value.OperationId == operationId && value.Status == PendingApproval,
                    cancellationToken))
            {
                result = CorrectionCommandResult.Conflict("A correction proposal is already awaiting review for this operation.");
                return;
            }

            var proposal = new OperationCorrectionProposal
            {
                Id = Guid.NewGuid(),
                OperationId = operationId,
                Status = PendingApproval,
                Reason = reason,
                SettlementMethod = NormalizeSettlement(command.SettlementMethod),
                SettlementAmount = command.SettlementAmount,
                CreateReplacementDraft = command.CreateReplacementDraft,
                RequesterId = requesterId,
                RequestedAt = now
            };
            _operations.OperationCorrectionProposals.Add(proposal);
            StageAudit(proposal.Id, requesterId, requesterRole, "OperationCorrection", "Requested", new
            {
                proposal.OperationId,
                proposal.Reason,
                proposal.SettlementMethod,
                proposal.SettlementAmount,
                proposal.CreateReplacementDraft
            }, now);
            StageOutbox(new OperationCorrectionChangedEvent(proposal.Id, proposal.OperationId, "Requested", requesterId, now));
            await _operations.SaveChangesAsync(cancellationToken);
            await _identity.SaveChangesAsync(cancellationToken);
            await _shared.SaveChangesAsync(cancellationToken);
            result = CorrectionCommandResult.Created(ToResponse(proposal));
        }, cancellationToken, _identity, _shared);

        return result ?? throw new InvalidOperationException("The correction request did not produce a result.");
    }

    public async Task<CorrectionCommandResult> SubmitSettlementAsync(
        Guid proposalId,
        SubmitOperationCorrectionSettlementCommand command,
        Guid requesterId,
        string? requesterRole,
        CancellationToken cancellationToken)
    {
        var proposal = await _operations.OperationCorrectionProposals.AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == proposalId, cancellationToken);
        if (proposal is null) return CorrectionCommandResult.NotFound();
        if (proposal.RequesterId != requesterId) return CorrectionCommandResult.Forbidden();
        if (proposal.Status != PendingApproval) return CorrectionCommandResult.Conflict("Only a pending proposal can receive a settlement.");

        var validation = ValidateSettlement(command.SettlementMethod, command.SettlementAmount);
        if (validation is not null) return validation;

        var settlementMethod = NormalizeSettlement(command.SettlementMethod);
        var settlementAmount = command.SettlementAmount;
        var now = _clock.EgyptNow;
        try
        {
            await SharedDbTransaction.ExecuteAsync(_operations, async () =>
            {
                proposal = await LoadProposalForUpdateAsync(proposalId, cancellationToken)
                    ?? throw new CorrectionBusinessException("The correction proposal no longer exists.", 404);
                if (proposal.RequesterId != requesterId) throw new CorrectionBusinessException("Only the requester can update this settlement.", 403);
                if (proposal.Status != PendingApproval) throw new CorrectionBusinessException("Only a pending proposal can receive a settlement.", 409);
                _ = await LoadOperationForUpdateAsync(proposal.OperationId, cancellationToken)
                    ?? throw new CorrectionBusinessException("The source operation no longer exists.", 404);
                proposal.SettlementMethod = settlementMethod;
                proposal.SettlementAmount = settlementAmount;
                StageAudit(proposal.Id, requesterId, requesterRole, "OperationCorrection", "SettlementSubmitted", new
                {
                    proposal.SettlementMethod,
                    proposal.SettlementAmount
                }, now);
                StageOutbox(new OperationCorrectionChangedEvent(proposal.Id, proposal.OperationId, "SettlementSubmitted", requesterId, now));
                await _operations.SaveChangesAsync(cancellationToken);
                await _identity.SaveChangesAsync(cancellationToken);
                await _shared.SaveChangesAsync(cancellationToken);
            }, cancellationToken, _identity, _shared);
        }
        catch (CorrectionBusinessException exception)
        {
            return new CorrectionCommandResult(exception.StatusCode, exception.Message, null, null, exception.Code);
        }

        return CorrectionCommandResult.Ok(ToResponse(proposal));
    }

    public async Task<CorrectionCommandResult> RejectAsync(
        Guid proposalId,
        RejectOperationCorrectionCommand command,
        Guid reviewerId,
        string? reviewerRole,
        CancellationToken cancellationToken)
    {
        var reason = command.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason)) return CorrectionCommandResult.Validation("reason", "A rejection reason is required.");

        var proposal = await _operations.OperationCorrectionProposals.AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == proposalId, cancellationToken);
        if (proposal is null) return CorrectionCommandResult.NotFound();
        if (proposal.RequesterId == reviewerId) return CorrectionCommandResult.Forbidden("Requesters cannot review their own correction proposal.");
        if (proposal.Status != PendingApproval) return CorrectionCommandResult.Conflict("Only a pending proposal can be rejected.");

        var now = _clock.EgyptNow;
        try
        {
            await SharedDbTransaction.ExecuteAsync(_operations, async () =>
            {
                proposal = await LoadProposalForUpdateAsync(proposalId, cancellationToken)
                    ?? throw new CorrectionBusinessException("The correction proposal no longer exists.", 404);
                if (proposal.RequesterId == reviewerId) throw new CorrectionBusinessException("Requesters cannot review their own correction proposal.", 403);
                if (proposal.Status != PendingApproval) throw new CorrectionBusinessException("Only a pending proposal can be rejected.", 409);
                _ = await LoadOperationForUpdateAsync(proposal.OperationId, cancellationToken)
                    ?? throw new CorrectionBusinessException("The source operation no longer exists.", 404);
                proposal.Status = Rejected;
                proposal.ReviewerId = reviewerId;
                proposal.ReviewedAt = now;
                proposal.RejectionReason = reason;
                StageAudit(proposal.Id, reviewerId, reviewerRole, "OperationCorrection", "Rejected", new { Reason = reason }, now);
                StageOutbox(new OperationCorrectionChangedEvent(proposal.Id, proposal.OperationId, "Rejected", reviewerId, now));
                await _operations.SaveChangesAsync(cancellationToken);
                await _identity.SaveChangesAsync(cancellationToken);
                await _shared.SaveChangesAsync(cancellationToken);
            }, cancellationToken, _identity, _shared);
        }
        catch (CorrectionBusinessException exception)
        {
            return new CorrectionCommandResult(exception.StatusCode, exception.Message, null, null, exception.Code);
        }

        return CorrectionCommandResult.Ok(ToResponse(proposal));
    }

    public async Task<CorrectionCommandResult> ApproveAsync(
        Guid proposalId,
        Guid reviewerId,
        string? reviewerRole,
        CancellationToken cancellationToken)
    {
        LenseeTelemetry.CorrectionRequests.Add(1, new KeyValuePair<string, object?>("operation", "approve"));
        if (reviewerId == Guid.Empty) return CorrectionCommandResult.Forbidden();

        var proposal = await _operations.OperationCorrectionProposals.AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == proposalId, cancellationToken);
        if (proposal is null) return CorrectionCommandResult.NotFound();
        if (proposal.RequesterId == reviewerId) return CorrectionCommandResult.Forbidden("Requesters cannot approve their own correction proposal.");
        if (proposal.Status != PendingApproval) return CorrectionCommandResult.Conflict("Only a pending proposal can be approved.");

        var now = _clock.EgyptNow;
        OperationCorrectionResponse? response = null;
        try
        {
            await SharedDbTransaction.ExecuteAsync(_operations, async () =>
            {
                proposal = await LoadProposalForUpdateAsync(proposalId, cancellationToken)
                    ?? throw new CorrectionBusinessException("The correction proposal no longer exists.", 404);
                if (proposal.RequesterId == reviewerId) throw new CorrectionBusinessException("Requesters cannot approve their own correction proposal.", 403);
                if (proposal.Status != PendingApproval) throw new CorrectionBusinessException("Only a pending proposal can be approved.", 409);
                var original = await LoadOperationForUpdateAsync(proposal.OperationId, cancellationToken);
                if (original is null) throw new CorrectionBusinessException("The source operation no longer exists.", 404);
                if (!IsFinalized(original.Status) || original.RecordKind != "Standard")
                {
                    throw new CorrectionBusinessException("The source operation is no longer eligible for correction.", 409);
                }
                if (await _operations.OperationLogs.AnyAsync(value =>
                        value.ReversesOperationId == original.Id && value.RecordKind == "Reversal" && !value.IsDeleted,
                        cancellationToken))
                {
                    throw new CorrectionBusinessException("A reversal already exists for this operation.", 409);
                }

                var payment = await LoadPaymentLogForUpdateAsync(original.Id, cancellationToken);
                await ValidateSettlementCapAsync(original, proposal, payment, cancellationToken);
                EnsureCompensationSupported(original);

                var reversal = CopyOperation(original, "Reversal", original.Id, null, proposal, reviewerId, now, original.Status);
                _operations.OperationLogs.Add(reversal);
                await CompensateStockAsync(original, reversal.Id, reviewerId, cancellationToken);
                await CreateSettlementAsync(original, proposal, payment, reviewerId, now, cancellationToken);

                OperationLog? replacement = null;
                if (proposal.CreateReplacementDraft)
                {
                    replacement = CopyOperation(original, "Replacement", null, original.Id, proposal, reviewerId, now, "Draft");
                    _operations.OperationLogs.Add(replacement);
                }

                proposal.Status = Approved;
                proposal.ReviewerId = reviewerId;
                proposal.ReviewedAt = now;
                proposal.ReversalOperationId = reversal.Id;
                proposal.ReplacementOperationId = replacement?.Id;

                StageAudit(proposal.Id, reviewerId, reviewerRole, "OperationCorrection", "Approved", new
                {
                    proposal.OperationId,
                    ReversalOperationId = reversal.Id,
                    ReplacementOperationId = replacement?.Id,
                    proposal.SettlementMethod,
                    proposal.SettlementAmount
                }, now);
                StageOutbox(new OperationCorrectionChangedEvent(proposal.Id, original.Id, "Approved", reviewerId, now));

                await _operations.SaveChangesAsync(cancellationToken);
                await _payments.SaveChangesAsync(cancellationToken);
                await _identity.SaveChangesAsync(cancellationToken);
                await _shared.SaveChangesAsync(cancellationToken);
                response = ToResponse(proposal);
            }, cancellationToken, _inventory, _payments, _identity, _shared);
        }
        catch (CorrectionBusinessException exception)
        {
            LenseeTelemetry.CorrectionFailures.Add(1, new KeyValuePair<string, object?>("reason", exception.StatusCode.ToString()));
            return new CorrectionCommandResult(exception.StatusCode, exception.Message, null, null, exception.Code);
        }
        catch (InvalidOperationException exception)
        {
            LenseeTelemetry.CorrectionFailures.Add(1, new KeyValuePair<string, object?>("reason", "validation"));
            return CorrectionCommandResult.Validation("correction", exception.Message);
        }

        return CorrectionCommandResult.Ok(response!);
    }

    public async Task<OperationCorrectionResponse?> GetAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        var proposal = await _operations.OperationCorrectionProposals.AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == proposalId, cancellationToken);
        return proposal is null ? null : ToResponse(proposal);
    }

    public async Task<IReadOnlyList<OperationCorrectionResponse>> GetLineageAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var proposals = await _operations.OperationCorrectionProposals.AsNoTracking()
            .Where(value => value.OperationId == operationId)
            .OrderByDescending(value => value.RequestedAt)
            .ToListAsync(cancellationToken);
        return proposals.Select(ToResponse).ToList();
    }

    private async Task<OperationLog?> LoadOperationForUpdateAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (!_operations.Database.IsRelational())
        {
            return await _operations.OperationLogs.Include(value => value.OperationLines)
                .FirstOrDefaultAsync(value => value.Id == operationId && !value.IsDeleted, cancellationToken);
        }

        return await _operations.OperationLogs
            // xmin is mapped as the operation concurrency token and PostgreSQL
            // does not expose system columns through SELECT *.  Include it in
            // the locked aggregate projection so correction commands work on
            // the same versioned row used by the rest of Operations.
            .FromSqlInterpolated($"select operations.operation_logs.*, xmin from operations.operation_logs where id = {operationId} and is_deleted = false for update")
            .Include(value => value.OperationLines)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<OperationCorrectionProposal?> LoadProposalForUpdateAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        if (!_operations.Database.IsRelational())
        {
            return await _operations.OperationCorrectionProposals.FirstOrDefaultAsync(value => value.Id == proposalId, cancellationToken);
        }

        return await _operations.OperationCorrectionProposals
            .FromSqlInterpolated($"select * from operations.operation_correction_proposals where id = {proposalId} for update")
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<MainPaymentLog?> LoadPaymentLogForUpdateAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (!_payments.Database.IsRelational())
        {
            return await _payments.MainPaymentLogs.FirstOrDefaultAsync(value => value.OperationId == operationId && !value.IsDeleted, cancellationToken);
        }

        return await _payments.MainPaymentLogs
            .FromSqlInterpolated($"select * from payments.main_payment_logs where operation_id = {operationId} and is_deleted = false for update")
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task ValidateSettlementCapAsync(OperationLog original, OperationCorrectionProposal proposal, MainPaymentLog? payment, CancellationToken cancellationToken)
    {
        var settledAmount = payment is null
            ? 0m
            : await PaymentFinancialCapacity.FinalizedPaidValueAsync(_payments, payment, cancellationToken);
        if (settledAmount > 0m && (proposal.SettlementMethod is null || proposal.SettlementAmount is null))
        {
            throw new CorrectionBusinessException("A completed payment requires a CashRefund or MerchantCredit settlement before approval.", 422);
        }
        if (proposal.SettlementMethod is null) return;
        if (proposal.SettlementAmount is null) throw new CorrectionBusinessException("Settlement amount is required.", 422);

        decimal remaining;
        if (proposal.SettlementMethod == CashRefund)
        {
            if (payment is null) throw new CorrectionBusinessException("Cash settlement requires an active payment log.", 409);
            remaining = await PaymentFinancialCapacity.CashRefundCapacityAsync(_payments, payment, null, cancellationToken);
        }
        else
        {
            if (original.ClientId is null) throw new CorrectionBusinessException("Merchant credit requires a source merchant.", 422);
            if (payment is null) throw new CorrectionBusinessException("Merchant credit requires an active payment log.", 409);
            remaining = await PaymentFinancialCapacity.MerchantCreditCapacityAsync(_payments, payment, null, cancellationToken);
        }

        if (proposal.SettlementAmount.Value > remaining)
        {
            throw new CorrectionBusinessException("The requested settlement exceeds the source operation's remaining refundable balance.", 409, "payment-cap-exceeded");
        }
    }

    private Task CreateSettlementAsync(OperationLog original, OperationCorrectionProposal proposal, MainPaymentLog? payment, Guid reviewerId, DateTime now, CancellationToken cancellationToken)
    {
        if (proposal.SettlementMethod is null || proposal.SettlementAmount is null) return Task.CompletedTask;
        if (proposal.SettlementMethod == CashRefund)
        {
            if (payment is null) throw new CorrectionBusinessException("Cash settlement requires an active payment log.", 409);
            var adjustment = new FinancialAdjustment
            {
                Id = Guid.NewGuid(),
                MerchantId = original.ClientId!.Value,
                OperationId = original.Id,
                PaymentLogId = payment.Id,
                AdjustmentType = CashRefund,
                Amount = proposal.SettlementAmount.Value,
                Status = "Completed",
                Notes = $"Approved correction {proposal.Id:N} for operation {original.OperationNumber}.",
                CreatedBy = reviewerId,
                CreatedAt = now,
                ReviewedBy = reviewerId,
                ReviewedAt = now,
                LineageKind = "OperationCorrection"
            };
            _payments.FinancialAdjustments.Add(adjustment);
            _payments.CashRecords.Add(new CashRecord
            {
                Id = Guid.NewGuid(),
                OperationId = original.Id,
                PaymentType = CashRefund,
                Amount = proposal.SettlementAmount.Value,
                Status = "Completed",
                PaymentDate = now,
                CreatedBy = reviewerId,
                FinancialAdjustmentId = adjustment.Id,
                Notes = $"Approved correction {proposal.Id:N} for operation {original.OperationNumber}."
            });
            return Task.CompletedTask;
        }

        if (payment is null) throw new CorrectionBusinessException("Merchant credit requires an active payment log.", 409);
        _payments.FinancialAdjustments.Add(new FinancialAdjustment
        {
            Id = Guid.NewGuid(),
            MerchantId = original.ClientId!.Value,
            OperationId = original.Id,
            PaymentLogId = payment.Id,
            AdjustmentType = MerchantCredit,
            Amount = proposal.SettlementAmount.Value,
            Status = "Completed",
            Notes = $"Approved correction {proposal.Id:N} for operation {original.OperationNumber}.",
            CreatedBy = reviewerId,
            CreatedAt = now,
            ReviewedBy = reviewerId,
            ReviewedAt = now,
            LineageKind = "OperationCorrection"
        });
        return Task.CompletedTask;
    }

    private async Task CompensateStockAsync(OperationLog original, Guid reversalOperationId, Guid reviewerId, CancellationToken cancellationToken)
    {
        var sourceLocation = original.SourceLocationId ?? original.DestinationLocationId;
        if (sourceLocation is null) throw new CorrectionBusinessException("The source operation does not have a stock location.", 422);

        switch (original.OperationType)
        {
            case "WholesaleSale":
            case "RetailSale":
                if (original.OperationLines.Any(value => value.EntryMode == "Pieces"))
                {
                    throw new CorrectionBusinessException("Piece-level sale corrections require exact loose-lot lineage and cannot be approved yet.", 409);
                }
                foreach (var line in original.OperationLines)
                {
                    await _ledger.ReceiveReturnAsync(sourceLocation.Value, line.SkuId, line.Quantity, reviewerId, line.LotNumber, line.ExpiryDate, $"Correction reversal of {original.OperationNumber}", reversalOperationId, cancellationToken);
                }
                break;
            case "InventoryReceipt":
            case "Return":
                foreach (var line in original.OperationLines)
                {
                    await _ledger.AdjustStocktakeBatchAsync(sourceLocation.Value, line.SkuId, line.LotNumber, line.ExpiryDate, -line.Quantity, reviewerId, reversalOperationId, cancellationToken);
                }
                break;
            case "WriteOff":
                foreach (var line in original.OperationLines)
                {
                    await _ledger.ReceiveReturnAsync(sourceLocation.Value, line.SkuId, line.Quantity, reviewerId, line.LotNumber, line.ExpiryDate, $"Correction reversal of {original.OperationNumber}", reversalOperationId, cancellationToken);
                }
                break;
            case "Reserve":
                foreach (var line in original.OperationLines)
                {
                    await _ledger.ReleaseWithRepUpToAsync(sourceLocation.Value, line.SkuId, line.Quantity, reviewerId, reversalOperationId, cancellationToken);
                }
                break;
            default:
                throw new CorrectionBusinessException($"A safe stock compensator is not yet defined for {original.OperationType}.", 409);
        }
    }

    private static void EnsureCompensationSupported(OperationLog operation)
    {
        if (operation.OperationType is not ("WholesaleSale" or "RetailSale" or "InventoryReceipt" or "Return" or "WriteOff" or "Reserve"))
        {
            throw new CorrectionBusinessException($"Corrections for {operation.OperationType} need a dedicated stock compensator before approval.", 409);
        }
    }

    private static OperationLog CopyOperation(
        OperationLog source,
        string recordKind,
        Guid? reversesOperationId,
        Guid? replacedOperationId,
        OperationCorrectionProposal proposal,
        Guid actorId,
        DateTime now,
        string status)
    {
        var id = Guid.NewGuid();
        return new OperationLog
        {
            Id = id,
            OperationNumber = $"OP-{(recordKind == "Reversal" ? "REV" : "RPL")}-{id:N}"[..Math.Min(50, $"OP-{(recordKind == "Reversal" ? "REV" : "RPL")}-{id:N}".Length)],
            OperationType = source.OperationType,
            Status = status,
            SourceLocationId = source.SourceLocationId,
            DestinationLocationId = source.DestinationLocationId,
            ClientId = source.ClientId,
            ClientName = source.ClientName,
            RepresentativeId = source.RepresentativeId,
            PaymentMethod = source.PaymentMethod,
            SalesChannel = source.SalesChannel,
            BuyerPhone = source.BuyerPhone,
            BuyerEmail = source.BuyerEmail,
            ShippingAddress = source.ShippingAddress,
            Notes = source.Notes,
            CreatedBy = actorId,
            CreatedActorName = "Correction workflow",
            CreatedAt = now,
            ConfirmedBy = status == "Draft" ? null : actorId,
            ConfirmedAt = status == "Draft" ? null : now,
            RecordKind = recordKind,
            ReversesOperationId = reversesOperationId,
            ReplacedOperationId = replacedOperationId,
            CorrectionProposalId = proposal.Id,
            CorrectionReason = proposal.Reason,
            CorrectedBy = actorId,
            CorrectedAt = now,
            OperationLines = source.OperationLines.Select(line => new OperationLine
            {
                Id = Guid.NewGuid(),
                OperationId = id,
                SkuId = line.SkuId,
                ProductNameSnapshot = line.ProductNameSnapshot,
                SkuCodeSnapshot = line.SkuCodeSnapshot,
                MerchantNameSnapshot = line.MerchantNameSnapshot,
                RepresentativeNameSnapshot = line.RepresentativeNameSnapshot,
                Section = line.Section,
                Quantity = line.Quantity,
                EntryMode = line.EntryMode,
                BonusQuantity = line.BonusQuantity,
                UnitPrice = line.UnitPrice,
                LineTotal = line.LineTotal,
                WriteOffReason = line.WriteOffReason,
                WriteOffReasonText = line.WriteOffReasonText,
                ExpiryDate = line.ExpiryDate,
                LotNumber = line.LotNumber,
                UnitCost = line.UnitCost,
                LineNotes = line.LineNotes
            }).ToList()
        };
    }

    private void StageAudit(Guid entityId, Guid actorId, string? actorRole, string entityType, string action, object fields, DateTime now)
    {
        _identity.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ChangedFields = JsonSerializer.Serialize(fields),
            UserId = actorId,
            ActorType = actorRole ?? "Unknown",
            ActorName = "Command-owned audit envelope",
            CreatedAt = now
        });
    }

    private void StageOutbox(OperationCorrectionChangedEvent appEvent)
    {
        _shared.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(OperationCorrectionChangedEvent).AssemblyQualifiedName ?? nameof(OperationCorrectionChangedEvent),
            EventVersion = 1,
            Payload = JsonSerializer.Serialize(appEvent),
            Status = "Pending",
            Attempts = 0,
            OccurredAt = appEvent.OccurredAt,
            NextAttemptAt = appEvent.OccurredAt
        });
    }

    private static bool IsFinalized(string status) => status is "Confirmed" or "Completed" or "Received";

    private static CorrectionCommandResult? ValidateSettlement(string? method, decimal? amount)
    {
        var normalized = NormalizeSettlement(method);
        if (normalized is null && amount is null) return null;
        if (normalized is null) return CorrectionCommandResult.Validation("settlementMethod", "Settlement method must be CashRefund or MerchantCredit.");
        if (amount is null || amount <= 0m) return CorrectionCommandResult.Validation("settlementAmount", "Settlement amount must be greater than zero.");
        return null;
    }

    private static string? NormalizeSettlement(string? method) => method?.Trim() switch
    {
        CashRefund => CashRefund,
        MerchantCredit => MerchantCredit,
        _ => null
    };

    private static OperationCorrectionResponse ToResponse(OperationCorrectionProposal proposal) => new(
        proposal.Id,
        proposal.OperationId,
        proposal.Status,
        proposal.Reason,
        proposal.SettlementMethod,
        proposal.SettlementAmount,
        proposal.CreateReplacementDraft,
        proposal.RequesterId,
        proposal.RequestedAt,
        proposal.ReviewerId,
        proposal.ReviewedAt,
        proposal.RejectionReason,
        proposal.ReversalOperationId,
        proposal.ReplacementOperationId);

    private sealed class CorrectionBusinessException : Exception
    {
        public CorrectionBusinessException(string message, int statusCode, string? code = null) : base(message)
        {
            StatusCode = statusCode;
            Code = code ?? (statusCode == StatusCodes.Status409Conflict ? "transition-conflict" : null);
        }
        public int StatusCode { get; }
        public string? Code { get; }
    }
}

public sealed record CreateOperationCorrectionCommand(
    string? Reason,
    string? SettlementMethod,
    decimal? SettlementAmount,
    bool CreateReplacementDraft);

public sealed record SubmitOperationCorrectionSettlementCommand(string? SettlementMethod, decimal? SettlementAmount);

public sealed record RejectOperationCorrectionCommand(string? Reason);

public sealed record OperationCorrectionResponse(
    Guid Id,
    Guid OperationId,
    string Status,
    string Reason,
    string? SettlementMethod,
    decimal? SettlementAmount,
    bool CreateReplacementDraft,
    Guid RequesterId,
    DateTime RequestedAt,
    Guid? ReviewerId,
    DateTime? ReviewedAt,
    string? RejectionReason,
    Guid? ReversalOperationId,
    Guid? ReplacementOperationId);

public sealed record CorrectionCommandResult(int StatusCode, string? Error, OperationCorrectionResponse? Value, string? ErrorField, string? Code = null)
{
    public static CorrectionCommandResult Created(OperationCorrectionResponse value) => new(201, null, value, null);
    public static CorrectionCommandResult Ok(OperationCorrectionResponse value) => new(200, null, value, null);
    public static CorrectionCommandResult NotFound() => new(404, "Correction proposal was not found.", null, null);
    public static CorrectionCommandResult Forbidden(string? message = null) => new(403, message ?? "This action is not permitted.", null, null);
    public static CorrectionCommandResult Conflict(string message) => new(409, message, null, null, "transition-conflict");
    public static CorrectionCommandResult Validation(string field, string message) => new(422, message, null, field);
}
