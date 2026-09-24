using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Modules.Payments.Data;

public partial class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<CashRecord> CashRecords { get; set; }

    public virtual DbSet<InstallmentSubLog> InstallmentSubLogs { get; set; }

    public virtual DbSet<FinancialAdjustment> FinancialAdjustments { get; set; }

    public virtual DbSet<PaymentIdempotencyKey> PaymentIdempotencyKeys { get; set; }

    public virtual DbSet<MainPaymentLog> MainPaymentLogs { get; set; }

    public virtual DbSet<MerchantReceivableAccount> MerchantReceivableAccounts { get; set; }

    public virtual DbSet<MerchantAccountEntry> MerchantAccountEntries { get; set; }

    public virtual DbSet<MerchantOperationObligation> MerchantOperationObligations { get; set; }

    public virtual DbSet<MerchantOpeningBalanceCharge> MerchantOpeningBalanceCharges { get; set; }

    public virtual DbSet<MerchantEntryAllocation> MerchantEntryAllocations { get; set; }
    public virtual DbSet<MerchantAllocationReconciliation> MerchantAllocationReconciliations { get; set; }

    public virtual DbSet<MerchantRefundReservation> MerchantRefundReservations { get; set; }

    public virtual DbSet<MerchantAccountClassificationSnapshot> MerchantAccountClassificationSnapshots { get; set; }

    public virtual DbSet<MerchantAccountCollectionDraft> MerchantAccountCollectionDrafts { get; set; }

    public virtual DbSet<PaymentAuditEvent> PaymentAuditEvents { get; set; }
    public virtual DbSet<MerchantFinancialClosureProposal> MerchantFinancialClosureProposals { get; set; }
    public virtual DbSet<MerchantFinancialClosureItem> MerchantFinancialClosureItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<MerchantFinancialClosureProposal>(entity =>
        {
            entity.ToTable("merchant_financial_closure_proposals", "payments", table =>
            {
                table.HasCheckConstraint("chk_financial_closure_proposal_status", "status in ('PendingAdminReview','PartiallyApproved','Approved','Rejected')");
            });
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.AccountId, x.Status });
            entity.HasIndex(x => new { x.AccountId, x.IdempotencyKey }).IsUnique().HasFilter("(idempotency_key IS NOT NULL)");
            entity.Property(x => x.Id).HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(x => x.Status).HasMaxLength(40).HasDefaultValue("PendingAdminReview");
            entity.Property(x => x.SubmittedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.ReviewedAt).HasColumnType("timestamp without time zone");
            entity.Property(x => x.IdempotencyKey).HasMaxLength(200);
            entity.HasMany(x => x.Items).WithOne(x => x.Proposal).HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MerchantFinancialClosureItem>(entity =>
        {
            entity.ToTable("merchant_financial_closure_items", "payments", table =>
            {
                table.HasCheckConstraint("chk_financial_closure_item_decision", "decision in ('Pending','Approved','Rejected')");
                table.HasCheckConstraint("chk_financial_closure_item_amount", "settlement_amount >= 0 and remaining_amount >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ProposalId, x.OperationId }).IsUnique();
            entity.HasIndex(x => x.OperationId);
            entity.Property(x => x.Id).HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(x => x.OperationNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SettlementAmount).HasPrecision(18, 4);
            entity.Property(x => x.RemainingAmount).HasPrecision(18, 4);
            entity.Property(x => x.Decision).HasMaxLength(20).HasDefaultValue("Pending");
            entity.Property(x => x.DecidedAt).HasColumnType("timestamp without time zone");
        });

        modelBuilder.Entity<CashRecord>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("cash_records_pkey");

            entity.ToTable("cash_records", "payments", table =>
            {
                table.HasCheckConstraint("chk_cash_payment_type", "payment_type in ('CashReceived','CashRefund')");
                table.HasCheckConstraint("chk_cash_status", "status in ('PendingAccountant','Completed','Cancelled')");
                table.HasCheckConstraint("chk_cash_amount", "amount > 0");
                table.HasCheckConstraint("chk_cash_record_scope", "operation_id is not null or merchant_id is not null");
            });

            entity.HasIndex(e => e.PaymentDate, "idx_cash_records_date").IsDescending();

            entity.HasIndex(e => e.OperationId, "idx_cash_records_operation");
            entity.HasIndex(e => e.MerchantId, "idx_cash_records_merchant").HasFilter("(merchant_id IS NOT NULL)");
            entity.HasIndex(e => e.FinancialAdjustmentId, "idx_cash_records_adjustment")
                .HasFilter("(financial_adjustment_id IS NOT NULL)");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 4)
                .HasColumnName("amount");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.ConfirmedBy).HasColumnName("confirmed_by");
            entity.Property(e => e.ConfirmedAt).HasColumnType("timestamp without time zone").HasColumnName("confirmed_at");
            entity.Property(e => e.FinancialAdjustmentId).HasColumnName("financial_adjustment_id");
            entity.Property(e => e.FinanceAccountId).HasColumnName("finance_account_id");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.MerchantId).HasColumnName("merchant_id");
            entity.Property(e => e.PaymentDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("payment_date");
            entity.Property(e => e.PaymentType)
                .HasMaxLength(50)
                .HasDefaultValueSql("'CashReceived'::character varying")
                .HasColumnName("payment_type");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Completed'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.SubType)
                .HasMaxLength(50)
                .HasColumnName("sub_type");
            entity.Property(e => e.TransactionReference).HasMaxLength(200).HasColumnName("transaction_reference");
            entity.Property(e => e.FinanceAccountId).HasColumnName("finance_account_id");
        });

        modelBuilder.Entity<InstallmentSubLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("installment_sub_logs_pkey");

            entity.ToTable("installment_sub_logs", "payments", table =>
            {
                table.HasCheckConstraint("chk_sub_log_status", "sub_log_status in ('Draft','PendingAdminReview','Confirmed','Rejected')");
                table.HasCheckConstraint("chk_sub_log_amount", "amount >= 0");
                table.HasCheckConstraint("chk_sub_log_payment_method", "payment_method is null or payment_method in ('CashTransaction','CashHandToHand','BankTransfer','Wallet','Installment')");
            });

            entity.HasIndex(e => e.MainLogId, "idx_sub_logs_main_log");

            entity.HasIndex(e => e.SubLogStatus, "idx_sub_logs_status");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 4)
                .HasColumnName("amount");
            entity.Property(e => e.ConfirmedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("confirmed_at");
            entity.Property(e => e.ConfirmedBy).HasColumnName("confirmed_by");
            entity.Property(e => e.DateReceived).HasColumnName("date_received");
            entity.Property(e => e.DraftedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("drafted_at");
            entity.Property(e => e.DraftedBy).HasColumnName("drafted_by");
            // This column was introduced by the Phase 2 finance migration.
            // Explicit mapping is required because the database convention is
            // snake_case while this legacy entity is otherwise convention-based.
            entity.Property(e => e.FinanceAccountId).HasColumnName("finance_account_id");
            entity.Property(e => e.MainLogId).HasColumnName("main_log_id");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(50)
                .HasColumnName("payment_method");
            entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
            entity.Property(e => e.TransactionReference).HasMaxLength(200).HasColumnName("transaction_reference");
            entity.Property(e => e.SubLogStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Draft'::character varying")
                .HasColumnName("sub_log_status");

            entity.HasOne(d => d.MainLog).WithMany(p => p.InstallmentSubLogs)
                .HasForeignKey(d => d.MainLogId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("installment_sub_logs_main_log_id_fkey");
        });

        modelBuilder.Entity<FinancialAdjustment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("financial_adjustments_pkey");

            entity.ToTable("financial_adjustments", "payments", table =>
            {
                table.HasCheckConstraint("chk_financial_adjustment_type", "adjustment_type in ('AdditionalCharge','BalanceReduction','CashRefund')");
                table.HasCheckConstraint("chk_financial_adjustment_status", "status in ('PendingApproval','Approved','Rejected','Completed','Cancelled','LegacyUnlinked')");
                table.HasCheckConstraint("chk_financial_adjustment_amount", "amount > 0");
            });

            entity.HasIndex(e => e.MerchantId, "idx_financial_adjustments_merchant");

            entity.HasIndex(e => e.OperationId, "idx_financial_adjustments_operation").HasFilter("(operation_id IS NOT NULL)");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AdjustmentType)
                .HasMaxLength(50)
                .HasColumnName("adjustment_type");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 4)
                .HasColumnName("amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.MerchantId).HasColumnName("merchant_id");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.PaymentLogId).HasColumnName("payment_log_id");
            entity.Property(e => e.ReversesAdjustmentId).HasColumnName("reverses_adjustment_id");
            entity.Property(e => e.ReviewedBy).HasColumnName("reviewed_by");
            entity.Property(e => e.ReviewedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("reviewed_at");
            entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
            entity.Property(e => e.LineageKind)
                .HasMaxLength(50)
                .HasDefaultValueSql("'SourceLinked'::character varying")
                .HasColumnName("lineage_kind");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'PendingApproval'::character varying")
                .HasColumnName("status");
        });

        modelBuilder.Entity<PaymentIdempotencyKey>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("payment_idempotency_keys_pkey");

            entity.ToTable("payment_idempotency_keys", "payments", table =>
            {
                table.HasCheckConstraint("chk_payment_idempotency_status", "status in ('Pending','Completed')");
            });

            entity.HasIndex(e => new { e.Key, e.Scope }, "uq_payment_idempotency_key_scope").IsUnique();
            entity.HasIndex(e => e.ExpiresAt, "idx_payment_idempotency_expires_at");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.Scope)
                .HasMaxLength(200)
                .HasColumnName("scope");
            entity.Property(e => e.RequestHash)
                .HasMaxLength(128)
                .HasColumnName("request_hash");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasColumnName("status");
            entity.Property(e => e.ResponseStatusCode).HasColumnName("response_status_code");
            entity.Property(e => e.ResponseBody)
                .HasColumnType("jsonb")
                .HasColumnName("response_body");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.LastSeenAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_seen_at");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("expires_at");
        });

        modelBuilder.Entity<MainPaymentLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("main_payment_logs_pkey");

            entity.ToTable("main_payment_logs", "payments", table =>
            {
                table.HasCheckConstraint("chk_main_payment_method", "payment_method is null or payment_method in ('CashHandToHand','CashTransaction','BankTransfer','Wallet')");
                table.HasCheckConstraint("chk_main_payment_scope", "scope in ('MerchantAccount','OtherPayments')");
                table.HasCheckConstraint("chk_main_payment_status", "status in ('PendingAdmin','PendingAccountant','PendingAdminReview','Completed','Rejected','Cancelled')");
                table.HasCheckConstraint("chk_main_payment_total_amount", "total_amount >= 0");
                table.HasCheckConstraint("chk_main_payment_amount_paid", "amount_paid >= 0");
                table.HasCheckConstraint("chk_main_payment_pending_amount", "pending_amount >= 0");
                table.HasCheckConstraint("chk_main_payment_paid_lte_total", "amount_paid + pending_amount <= total_amount");
            });

            entity.HasIndex(e => e.AssignedTo, "idx_main_payment_assigned").HasFilter("(assigned_to IS NOT NULL)");

            entity.HasIndex(e => e.MerchantId, "idx_main_payment_merchant");

            entity.HasIndex(e => e.OperationId, "idx_main_payment_operation");

            entity.HasIndex(e => e.OperationId, "uq_main_payment_operation_active")
                .IsUnique()
                .HasFilter("(is_deleted = false)");

            entity.HasIndex(e => e.Status, "idx_main_payment_status").HasFilter("(is_deleted = false)");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AmountPaid)
                .HasPrecision(18, 4)
                .HasColumnName("amount_paid");
            entity.Property(e => e.AssignedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("assigned_at");
            entity.Property(e => e.AssignedTo).HasColumnName("assigned_to");
            entity.Property(e => e.InitializedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("initialized_at");
            entity.Property(e => e.InitializedBy).HasColumnName("initialized_by");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.LastModifiedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_modified_at");
            entity.Property(e => e.LastModifiedBy).HasColumnName("last_modified_by");
            entity.Property(e => e.MerchantId).HasColumnName("merchant_id");
            entity.Property(e => e.Scope)
                .HasMaxLength(30)
                .HasDefaultValue("OtherPayments")
                .HasColumnName("scope");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(50)
                .HasColumnName("payment_method");
            entity.Property(e => e.PendingAmount)
                .HasPrecision(18, 4)
                .HasDefaultValue(0m)
                .HasColumnName("pending_amount");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'PendingAdmin'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TotalAmount)
                .HasPrecision(18, 4)
                .HasColumnName("total_amount");
        });

        modelBuilder.Entity<MerchantReceivableAccount>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.ToTable("merchant_receivable_accounts", "payments", table =>
            {
                table.HasCheckConstraint("chk_merchant_account_status", "status in ('Open','Frozen','Closed')");
                table.HasCheckConstraint("chk_merchant_account_next_sequence", "next_sequence > 0");
            });
            entity.HasIndex(value => value.MerchantId).IsUnique();
            entity.Property(value => value.Status).HasMaxLength(30).HasColumnName("status");
            entity.Property(value => value.NextSequence).HasColumnName("next_sequence");
            entity.Property(value => value.MerchantId).HasColumnName("merchant_id");
            entity.Property(value => value.OpenedAt).HasColumnType("timestamp without time zone").HasColumnName("opened_at");
            entity.Property(value => value.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
            entity.Property(value => value.OpenedBy).HasColumnName("opened_by");
        });

        modelBuilder.Entity<MerchantAccountCollectionDraft>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.ToTable("merchant_account_collection_drafts", "payments", table =>
            {
                table.HasCheckConstraint("chk_merchant_collection_draft_amount", "amount > 0");
                table.HasCheckConstraint("chk_merchant_collection_draft_scope", "scope = 'MerchantAccount'");
                table.HasCheckConstraint("chk_merchant_collection_draft_status", "status in ('Draft','PendingAdminReview','Confirmed','Rejected')");
                table.HasCheckConstraint("chk_merchant_collection_draft_method", "payment_method in ('CashHandToHand','CashTransaction','BankTransfer','Wallet')");
            });
            entity.HasIndex(value => new { value.AccountId, value.Status, value.DraftedAt });
            entity.HasIndex(value => value.AssignedTo).HasFilter("(assigned_to IS NOT NULL)");
            entity.Property(value => value.AccountId).HasColumnName("account_id");
            entity.Property(value => value.Scope).HasMaxLength(30).HasDefaultValue("MerchantAccount").HasColumnName("scope");
            entity.Property(value => value.SourceOperationId).HasColumnName("source_operation_id");
            entity.Property(value => value.Amount).HasPrecision(18, 4).HasColumnName("amount");
            entity.Property(value => value.PaymentMethod).HasMaxLength(50).HasColumnName("payment_method");
            entity.Property(value => value.TransactionReference).HasMaxLength(200).HasColumnName("transaction_reference");
            entity.Property(value => value.FinanceAccountId).HasColumnName("finance_account_id");
            entity.Property(value => value.AllocationsJson).HasColumnType("jsonb").HasColumnName("allocations_json");
            entity.Property(value => value.Notes).HasColumnName("notes");
            entity.Property(value => value.Status).HasMaxLength(50).HasColumnName("status");
            entity.Property(value => value.DraftedBy).HasColumnName("drafted_by");
            entity.Property(value => value.DraftedAt).HasColumnType("timestamp without time zone").HasColumnName("drafted_at");
            entity.Property(value => value.AssignedTo).HasColumnName("assigned_to");
            entity.Property(value => value.AssignedAt).HasColumnType("timestamp without time zone").HasColumnName("assigned_at");
            entity.Property(value => value.ConfirmedBy).HasColumnName("confirmed_by");
            entity.Property(value => value.ConfirmedAt).HasColumnType("timestamp without time zone").HasColumnName("confirmed_at");
            entity.Property(value => value.RejectionReason).HasColumnName("rejection_reason");
            entity.HasOne(value => value.Account).WithMany().HasForeignKey(value => value.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentAuditEvent>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.ToTable("payment_audit_events", "payments", table =>
            {
                table.HasCheckConstraint("chk_payment_audit_amount", "amount is null or amount >= 0");
            });
            entity.HasIndex(value => value.OccurredAt).IsDescending();
            entity.HasIndex(value => new { value.MerchantId, value.OccurredAt });
            entity.HasIndex(value => new { value.PaymentLogId, value.OccurredAt });
            entity.HasIndex(value => new { value.CollectionDraftId, value.OccurredAt });
            entity.Property(value => value.Action).HasMaxLength(80).HasColumnName("action");
            entity.Property(value => value.PreviousStatus).HasMaxLength(50).HasColumnName("previous_status");
            entity.Property(value => value.NewStatus).HasMaxLength(50).HasColumnName("new_status");
            entity.Property(value => value.MerchantId).HasColumnName("merchant_id");
            entity.Property(value => value.OperationId).HasColumnName("operation_id");
            entity.Property(value => value.PaymentLogId).HasColumnName("payment_log_id");
            entity.Property(value => value.CollectionDraftId).HasColumnName("collection_draft_id");
            entity.Property(value => value.ActorId).HasColumnName("actor_id");
            entity.Property(value => value.OccurredAt).HasColumnType("timestamp without time zone").HasColumnName("occurred_at");
            entity.Property(value => value.Amount).HasPrecision(18, 4).HasColumnName("amount");
            entity.Property(value => value.PaymentMethod).HasMaxLength(50).HasColumnName("payment_method");
            entity.Property(value => value.Reason).HasColumnName("reason");
            entity.Property(value => value.CorrelationId).HasMaxLength(200).HasColumnName("correlation_id");
            entity.Property(value => value.IdempotencyKey).HasMaxLength(200).HasColumnName("idempotency_key");
            entity.Property(value => value.DataJson).HasColumnType("jsonb").HasColumnName("data_json");
        });

        modelBuilder.Entity<MerchantAccountEntry>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.ToTable("merchant_account_entries", "payments", table =>
            {
                table.HasCheckConstraint("chk_merchant_account_entry_amount", "(debit_amount > 0 and credit_amount = 0) or (credit_amount > 0 and debit_amount = 0)");
                table.HasCheckConstraint("chk_merchant_account_entry_status", "status in ('Posted','Reversed')");
                table.HasCheckConstraint("chk_merchant_account_entry_method", "payment_method is null or payment_method in ('CashHandToHand','CashTransaction','BankTransfer','Wallet')");
            });
            entity.HasIndex(value => new { value.AccountId, value.Sequence }).IsUnique();
            entity.HasIndex(value => new { value.SourceType, value.SourceId, value.EntryType }).IsUnique();
            entity.HasIndex(value => value.OperationId);
            entity.Property(value => value.AccountId).HasColumnName("account_id");
            entity.Property(value => value.Sequence).HasColumnName("sequence");
            entity.Property(value => value.EntryType).HasMaxLength(50).HasColumnName("entry_type");
            entity.Property(value => value.DebitAmount).HasPrecision(18, 4).HasColumnName("debit_amount");
            entity.Property(value => value.CreditAmount).HasPrecision(18, 4).HasColumnName("credit_amount");
            entity.Property(value => value.PaymentMethod).HasMaxLength(50).HasColumnName("payment_method");
            entity.Property(value => value.TransactionReference).HasMaxLength(200).HasColumnName("transaction_reference");
            entity.Property(value => value.SourceType).HasMaxLength(80).HasColumnName("source_type");
            entity.Property(value => value.SourceId).HasColumnName("source_id");
            entity.Property(value => value.OperationId).HasColumnName("operation_id");
            entity.Property(value => value.PaymentId).HasColumnName("payment_id");
            entity.Property(value => value.ReversesEntryId).HasColumnName("reverses_entry_id");
            entity.Property(value => value.Status).HasMaxLength(30).HasColumnName("status");
            entity.Property(value => value.PostedBy).HasColumnName("posted_by");
            entity.Property(value => value.PostedAt).HasColumnType("timestamp without time zone").HasColumnName("posted_at");
            entity.Property(value => value.Notes).HasColumnName("notes");
            entity.HasOne(value => value.Account).WithMany(value => value.Entries).HasForeignKey(value => value.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MerchantOperationObligation>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.ToTable("merchant_operation_obligations", "payments", table => table.HasCheckConstraint("chk_merchant_obligation_amount", "original_amount >= 0"));
            entity.HasIndex(value => value.OperationId).IsUnique().HasFilter("(operation_id IS NOT NULL)");
            entity.HasIndex(value => new { value.SourceType, value.SourceId }).IsUnique();
            entity.HasIndex(value => new { value.AccountId, value.Status, value.PostedAt });
            entity.Property(value => value.AccountId).HasColumnName("account_id");
            entity.Property(value => value.OperationId).HasColumnName("operation_id");
            entity.Property(value => value.SourceType).HasMaxLength(50).HasColumnName("source_type");
            entity.Property(value => value.SourceId).HasColumnName("source_id");
            entity.Property(value => value.OriginalAmount).HasPrecision(18, 4).HasColumnName("original_amount");
            entity.Property(value => value.PostedAt).HasColumnType("timestamp without time zone").HasColumnName("posted_at");
            entity.Property(value => value.Status).HasMaxLength(30).HasColumnName("status");
            entity.HasOne(value => value.Account).WithMany().HasForeignKey(value => value.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MerchantOpeningBalanceCharge>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.ToTable("merchant_opening_balance_charges", "payments", table =>
            {
                table.HasCheckConstraint("chk_opening_balance_amount", "amount > 0");
                table.HasCheckConstraint("chk_opening_balance_status", "status in ('Draft','PendingReview','Posted','Rejected','Reversed','Corrected')");
            });
            entity.HasIndex(value => new { value.MerchantId, value.Status });
            entity.HasIndex(value => value.MerchantId).IsUnique()
                .HasDatabaseName("ux_merchant_opening_one_root")
                .HasFilter("(reverses_charge_id IS NULL AND status <> 'Rejected')");
            entity.HasIndex(value => value.PostedEntryId).IsUnique().HasFilter("(posted_entry_id IS NOT NULL)");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.MerchantId).HasColumnName("merchant_id");
            entity.Property(value => value.Amount).HasPrecision(18, 4).HasColumnName("amount");
            entity.Property(value => value.AsOfDate).HasColumnName("as_of_date");
            entity.Property(value => value.Description).HasColumnName("description");
            entity.Property(value => value.Status).HasMaxLength(30).HasColumnName("status");
            entity.Property(value => value.CreatedBy).HasColumnName("created_by");
            entity.Property(value => value.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(value => value.ReviewedBy).HasColumnName("reviewed_by");
            entity.Property(value => value.ReviewedAt).HasColumnType("timestamp without time zone").HasColumnName("reviewed_at");
            entity.Property(value => value.ReviewReason).HasColumnName("review_reason");
            entity.Property(value => value.PostedEntryId).HasColumnName("posted_entry_id");
            entity.Property(value => value.ReversesChargeId).HasColumnName("reverses_charge_id");
            entity.Property(value => value.ReplacedByChargeId).HasColumnName("replaced_by_charge_id");
            entity.Property(value => value.CorrelationId).HasMaxLength(200).HasColumnName("correlation_id");
        });

        modelBuilder.Entity<MerchantEntryAllocation>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.ToTable("merchant_entry_allocations", "payments", table => table.HasCheckConstraint("chk_merchant_entry_allocation_amount", "amount > 0"));
            entity.HasIndex(value => new { value.EntryId, value.ObligationId }).IsUnique();
            entity.Property(value => value.EntryId).HasColumnName("entry_id");
            entity.Property(value => value.ObligationId).HasColumnName("obligation_id");
            entity.Property(value => value.Amount).HasPrecision(18, 4).HasColumnName("amount");
            entity.Property(value => value.AllocatedAt).HasColumnType("timestamp without time zone").HasColumnName("allocated_at");
            entity.Property(value => value.AllocatedBy).HasColumnName("allocated_by");
            entity.HasOne(value => value.Entry).WithMany(value => value.Allocations).HasForeignKey(value => value.EntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(value => value.Obligation).WithMany(value => value.Allocations).HasForeignKey(value => value.ObligationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MerchantAllocationReconciliation>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.ToTable("merchant_allocation_reconciliations", "payments");
            entity.HasIndex(value => value.SourceAllocationId).IsUnique();
            entity.HasIndex(value => value.ReplacementAllocationId).IsUnique();
            entity.HasIndex(value => new { value.CorrectionChargeId, value.CorrelationId }).IsUnique();
            entity.Property(value => value.CorrelationId).HasMaxLength(200).HasColumnName("correlation_id");
            entity.Property(value => value.SourceAllocationId).HasColumnName("source_allocation_id");
            entity.Property(value => value.ReplacementAllocationId).HasColumnName("replacement_allocation_id");
            entity.Property(value => value.CorrectionChargeId).HasColumnName("correction_charge_id");
            entity.Property(value => value.CreatedBy).HasColumnName("created_by");
            entity.Property(value => value.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.HasOne(value => value.SourceAllocation).WithMany().HasForeignKey(value => value.SourceAllocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(value => value.ReplacementAllocation).WithMany().HasForeignKey(value => value.ReplacementAllocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(value => value.CorrectionCharge).WithMany().HasForeignKey(value => value.CorrectionChargeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MerchantRefundReservation>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.ToTable("merchant_refund_reservations", "payments", table =>
            {
                table.HasCheckConstraint("chk_merchant_refund_reservation_amount", "amount > 0");
                table.HasCheckConstraint("chk_merchant_refund_reservation_status", "status in ('PendingApproval','Approved','PartiallyPaid','Paid','Rejected','Cancelled')");
                table.HasCheckConstraint("chk_merchant_refund_reservation_paid_amount", "paid_amount >= 0 and paid_amount <= amount");
            });
            entity.HasIndex(value => new { value.AccountId, value.Status });
            entity.Property(value => value.AccountId).HasColumnName("account_id");
            entity.Property(value => value.Amount).HasPrecision(18, 4).HasColumnName("amount");
            entity.Property(value => value.PaidAmount).HasPrecision(18, 4).HasDefaultValue(0m).HasColumnName("paid_amount");
            entity.Property(value => value.Status).HasMaxLength(30).HasColumnName("status");
            entity.Property(value => value.CreatedBy).HasColumnName("created_by");
            entity.Property(value => value.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(value => value.ApprovedBy).HasColumnName("approved_by");
            entity.Property(value => value.ApprovedAt).HasColumnType("timestamp without time zone").HasColumnName("approved_at");
            entity.Property(value => value.PayoutEntryId).HasColumnName("payout_entry_id");
            entity.Property(value => value.Notes).HasColumnName("notes");
        });

        modelBuilder.Entity<MerchantAccountClassificationSnapshot>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.ToTable("merchant_account_classification_snapshots", "payments");
            entity.HasIndex(value => new { value.AccountId, value.CalendarYear }).IsUnique();
            entity.Property(value => value.AccountId).HasColumnName("account_id");
            entity.Property(value => value.CalendarYear).HasColumnName("calendar_year");
            entity.Property(value => value.IsPartialYear).HasColumnName("is_partial_year");
            entity.Property(value => value.Grade).HasMaxLength(10).HasColumnName("grade");
            entity.Property(value => value.Score).HasPrecision(7, 2).HasColumnName("score");
            entity.Property(value => value.FlagsJson).HasColumnType("jsonb").HasColumnName("flags_json");
            entity.Property(value => value.SettingsVersion).HasMaxLength(100).HasColumnName("settings_version");
            entity.Property(value => value.CalculatedAt).HasColumnType("timestamp without time zone").HasColumnName("calculated_at");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
