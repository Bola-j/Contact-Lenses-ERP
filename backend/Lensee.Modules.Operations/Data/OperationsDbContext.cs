using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Modules.Operations.Data;

public partial class OperationsDbContext : DbContext
{
    public OperationsDbContext(DbContextOptions<OperationsDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<InventoryReceiptHeader> InventoryReceiptHeaders { get; set; }

    public virtual DbSet<MerchantExpiryRecall> MerchantExpiryRecalls { get; set; }

    public virtual DbSet<OperationLine> OperationLines { get; set; }

    public virtual DbSet<OperationCorrectionProposal> OperationCorrectionProposals { get; set; }

    public virtual DbSet<OperationLog> OperationLogs { get; set; }

    public virtual DbSet<OperationVersion> OperationVersions { get; set; }

    public virtual DbSet<ReplenishmentRun> ReplenishmentRuns { get; set; }

    public virtual DbSet<StocktakeAdjustmentLine> StocktakeAdjustmentLines { get; set; }

    public virtual DbSet<StocktakeSession> StocktakeSessions { get; set; }

    public virtual DbSet<SupplyShipment> SupplyShipments { get; set; }

    public virtual DbSet<SupplyShipmentCost> SupplyShipmentCosts { get; set; }

    public virtual DbSet<SupplyShipmentHistory> SupplyShipmentHistoryLogs { get; set; }

    public virtual DbSet<SupplyShipmentLine> SupplyShipmentLines { get; set; }

    public virtual DbSet<ShopifyOrderLink> ShopifyOrderLinks { get; set; }

    public virtual DbSet<ShopifyWebhookEvent> ShopifyWebhookEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<InventoryReceiptHeader>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("inventory_receipt_headers_pkey");

            entity.ToTable("inventory_receipt_headers", "operations");

            entity.HasIndex(e => e.OperationId, "idx_receipt_headers_operation");

            entity.HasIndex(e => e.OperationId, "inventory_receipt_headers_operation_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.InvoiceNumber)
                .HasMaxLength(100)
                .HasColumnName("invoice_number");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.ReceiptDate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("receipt_date");
            entity.Property(e => e.SupplierName)
                .HasMaxLength(255)
                .HasColumnName("supplier_name");

            entity.HasOne(d => d.Operation).WithOne(p => p.InventoryReceiptHeader)
                .HasForeignKey<InventoryReceiptHeader>(d => d.OperationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("inventory_receipt_headers_operation_id_fkey");
        });

        modelBuilder.Entity<MerchantExpiryRecall>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("merchant_expiry_recalls_pkey");

            entity.ToTable("merchant_expiry_recalls", "operations", table =>
            {
                table.HasCheckConstraint("chk_merchant_expiry_recall_status", "status in ('Active','Completed','NoStock')");
                table.HasCheckConstraint("chk_merchant_expiry_recall_quantities", "sold_quantity >= 0 and returned_quantity >= 0");
            });

            entity.HasIndex(e => new { e.MerchantId, e.SkuId, e.LotNumber, e.ExpiryDate }, "uq_merchant_expiry_recall_batch").IsUnique();
            entity.HasIndex(e => new { e.Status, e.ExpiryDate }, "idx_merchant_expiry_recall_status_expiry");
            entity.HasIndex(e => e.MerchantId, "idx_merchant_expiry_recall_merchant");

            entity.Property(e => e.Id).HasDefaultValueSql("uuid_generate_v4()").HasColumnName("id");
            entity.Property(e => e.MerchantId).HasColumnName("merchant_id");
            entity.Property(e => e.SkuId).HasColumnName("sku_id");
            entity.Property(e => e.LotNumber).HasMaxLength(100).HasDefaultValue(string.Empty).HasColumnName("lot_number");
            entity.Property(e => e.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Active").HasColumnName("status");
            entity.Property(e => e.SoldQuantity).HasColumnName("sold_quantity");
            entity.Property(e => e.ReturnedQuantity).HasColumnName("returned_quantity");
            entity.Property(e => e.ResolvedSoldQuantity).HasColumnName("resolved_sold_quantity");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
            entity.Property(e => e.ResolvedAt).HasColumnType("timestamp without time zone").HasColumnName("resolved_at");
            entity.Property(e => e.ResolvedBy).HasColumnName("resolved_by");
            entity.Property(e => e.ResolutionNote).HasMaxLength(1000).HasColumnName("resolution_note");
        });

        modelBuilder.Entity<OperationLine>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("operation_lines_pkey");

            entity.ToTable("operation_lines", "operations", table =>
            {
                table.HasCheckConstraint("chk_operation_lines_entry_mode", "entry_mode in ('Packs','Pieces')");
                table.HasCheckConstraint("chk_operation_lines_section", "section in ('Standard','ChangeOut','ChangeIn')");
                table.HasCheckConstraint("chk_operation_lines_quantity", "quantity >= 0");
                table.HasCheckConstraint("chk_operation_lines_bonus_quantity", "bonus_quantity >= 0");
                table.HasCheckConstraint("chk_operation_lines_unit_price", "unit_price >= 0");
                table.HasCheckConstraint("chk_operation_lines_line_total", "line_total >= 0");
                table.HasCheckConstraint("chk_operation_lines_unit_cost", "unit_cost is null or unit_cost >= 0");
            });

            entity.HasIndex(e => e.OperationId, "idx_op_lines_operation");

            entity.HasIndex(e => e.SkuId, "idx_op_lines_sku");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.BonusQuantity)
                .HasDefaultValue(0)
                .HasColumnName("bonus_quantity");
            entity.Property(e => e.EntryMode)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Pieces'::character varying")
                .HasColumnName("entry_mode");
            entity.Property(e => e.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(e => e.LineNotes).HasColumnName("line_notes");
            entity.Property(e => e.LineTotal)
                .HasPrecision(18, 4)
                .HasColumnName("line_total");
            entity.Property(e => e.LotNumber)
                .HasMaxLength(100)
                .HasColumnName("lot_number");
            entity.Property(e => e.MerchantNameSnapshot)
                .HasMaxLength(255)
                .HasColumnName("merchant_name_snapshot");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.ProductNameSnapshot)
                .HasMaxLength(255)
                .HasColumnName("product_name_snapshot");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.RepresentativeNameSnapshot)
                .HasMaxLength(255)
                .HasColumnName("representative_name_snapshot");
            entity.Property(e => e.Section)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Standard'::character varying")
                .HasColumnName("section");
            entity.Property(e => e.ShopifyLineItemId).HasMaxLength(100).HasColumnName("shopify_line_item_id");
            entity.Property(e => e.ShopifyPropertiesSnapshot).HasColumnType("jsonb").HasColumnName("shopify_properties_snapshot");
            entity.Property(e => e.ShopifySkuSnapshot).HasMaxLength(255).HasColumnName("shopify_sku_snapshot");
            entity.Property(e => e.ShopifyTitleSnapshot).HasMaxLength(255).HasColumnName("shopify_title_snapshot");
            entity.Property(e => e.ShopifyVariantId).HasMaxLength(100).HasColumnName("shopify_variant_id");
            entity.Property(e => e.ShopifyVariantTitleSnapshot).HasMaxLength(255).HasColumnName("shopify_variant_title_snapshot");
            entity.Property(e => e.SkuCodeSnapshot)
                .HasMaxLength(100)
                .HasColumnName("sku_code_snapshot");
            entity.Property(e => e.SkuId).HasColumnName("sku_id");
            entity.Property(e => e.UnitCost)
                .HasPrecision(18, 4)
                .HasColumnName("unit_cost");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(18, 4)
                .HasColumnName("unit_price");
            entity.Property(e => e.WriteOffReason)
                .HasMaxLength(50)
                .HasColumnName("write_off_reason");
            entity.Property(e => e.WriteOffReasonText).HasColumnName("write_off_reason_text");

            entity.HasOne(d => d.Operation).WithMany(p => p.OperationLines)
                .HasForeignKey(d => d.OperationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("operation_lines_operation_id_fkey");
        });

        modelBuilder.Entity<OperationLog>(entity =>
        {
            entity.Property(e => e.ConcurrencyVersion).HasColumnName("xmin").IsRowVersion();
            entity.HasKey(e => e.Id).HasName("operation_logs_pkey");

            entity.ToTable("operation_logs", "operations", table =>
            {
                table.HasCheckConstraint(
                    "chk_op_type",
                    "operation_type in ('InventoryReceipt','WarehouseTransfer','WholesaleSale','RetailSale','Reserve','WriteOff','StocktakeAdjustment','Change','Return')");
                table.HasCheckConstraint(
                    "chk_op_status",
                    "status in ('Draft','Confirmed','Completed','Reserved','Shipped','Received','Cancelled')");
                table.HasCheckConstraint(
                    "chk_op_payment_method",
                    "payment_method is null or payment_method in ('CashHandToHand','CashTransaction','Installment')");
                table.HasCheckConstraint(
                    "chk_operation_record_kind",
                    "record_kind in ('Standard','Reversal','Replacement')");
            });

            entity.HasIndex(e => e.ClientId, "idx_op_logs_client").HasFilter("(client_id IS NOT NULL)");

            entity.HasIndex(e => e.CreatedAt, "idx_op_logs_created_at").IsDescending();

            entity.HasIndex(e => e.CreatedBy, "idx_op_logs_created_by");

            entity.HasIndex(e => e.SourceLocationId, "idx_op_logs_source_location");

            entity.HasIndex(e => new { e.OperationType, e.Status }, "idx_op_logs_type_status");

            entity.HasIndex(e => e.OperationNumber, "operation_logs_operation_number_key").IsUnique();

            entity.HasIndex(e => e.SalesChannel, "idx_op_logs_sales_channel");

            entity.HasIndex(e => e.MerchantExpiryRecallId, "idx_op_logs_merchant_expiry_recall");

            entity.HasIndex(e => e.ReversesOperationId, "uq_operation_active_reversal")
                .IsUnique()
                .HasFilter("(record_kind = 'Reversal' AND is_deleted = false)");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ClientId).HasColumnName("client_id");
            entity.Property(e => e.ClientName)
                .HasMaxLength(255)
                .HasColumnName("client_name");
            entity.Property(e => e.BuyerEmail).HasMaxLength(255).HasColumnName("buyer_email");
            entity.Property(e => e.BuyerPhone).HasMaxLength(50).HasColumnName("buyer_phone");
            entity.Property(e => e.ConfirmedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("confirmed_at");
            entity.Property(e => e.ConfirmedBy).HasColumnName("confirmed_by");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreatedActorName).HasMaxLength(100).HasColumnName("created_actor_name");
            entity.Property(e => e.CurrentVersionId).HasColumnName("current_version_id");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.DestinationLocationId).HasColumnName("destination_location_id");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OperationNumber)
                .HasMaxLength(50)
                .HasDefaultValueSql("('OP-'::text || to_char(nextval('operations.operation_number_seq'::regclass), 'FM000000'::text))")
                .HasColumnName("operation_number");
            entity.Property(e => e.OperationType)
                .HasMaxLength(50)
                .HasColumnName("operation_type");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(50)
                .HasColumnName("payment_method");
            entity.Property(e => e.MerchantExpiryRecallId).HasColumnName("merchant_expiry_recall_id");
            entity.Property(e => e.AutomationType).HasMaxLength(50).HasColumnName("automation_type");
            entity.Property(e => e.SalesChannel).HasMaxLength(50).HasDefaultValue("Manual").HasColumnName("sales_channel");
            entity.Property(e => e.ShippingAddress).HasColumnName("shipping_address");
            entity.Property(e => e.RepresentativeId).HasColumnName("representative_id");
            entity.Property(e => e.SourceLocationId).HasColumnName("source_location_id");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Draft'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.RecordKind)
                .HasMaxLength(30)
                .HasDefaultValue("Standard")
                .HasColumnName("record_kind");
            entity.Property(e => e.ReversesOperationId).HasColumnName("reverses_operation_id");
            entity.Property(e => e.ReplacedOperationId).HasColumnName("replaced_operation_id");
            entity.Property(e => e.CorrectionProposalId).HasColumnName("correction_proposal_id");
            entity.Property(e => e.CorrectionReason).HasColumnName("correction_reason");
            entity.Property(e => e.CorrectedBy).HasColumnName("corrected_by");
            entity.Property(e => e.CorrectedAt).HasColumnType("timestamp without time zone").HasColumnName("corrected_at");

            entity.HasOne(d => d.CurrentVersion).WithMany(p => p.OperationLogs)
                .HasForeignKey(d => d.CurrentVersionId)
                .HasConstraintName("fk_current_version");

            entity.HasOne(d => d.MerchantExpiryRecall).WithMany()
                .HasForeignKey(d => d.MerchantExpiryRecallId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("operation_logs_merchant_expiry_recall_id_fkey");
        });

        modelBuilder.Entity<OperationCorrectionProposal>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("operation_correction_proposals_pkey");
            entity.ToTable("operation_correction_proposals", "operations", table =>
            {
                table.HasCheckConstraint("chk_operation_correction_status", "status in ('PendingApproval','Approved','Rejected')");
                table.HasCheckConstraint("chk_operation_correction_settlement", "settlement_method is null or settlement_method in ('CashRefund','MerchantCredit')");
                table.HasCheckConstraint("chk_operation_correction_amount", "settlement_amount is null or settlement_amount > 0");
            });
            entity.HasIndex(e => e.OperationId, "idx_operation_corrections_operation");
            entity.HasIndex(e => e.OperationId, "uq_operation_active_correction")
                .IsUnique()
                .HasFilter("(status = 'PendingApproval')");
            entity.Property(e => e.Id).HasDefaultValueSql("uuid_generate_v4()").HasColumnName("id");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.Status).HasMaxLength(30).HasColumnName("status");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.SettlementMethod).HasMaxLength(30).HasColumnName("settlement_method");
            entity.Property(e => e.SettlementAmount).HasPrecision(18, 4).HasColumnName("settlement_amount");
            entity.Property(e => e.CreateReplacementDraft).HasDefaultValue(false).HasColumnName("create_replacement_draft");
            entity.Property(e => e.RequesterId).HasColumnName("requester_id");
            entity.Property(e => e.RequestedAt).HasColumnType("timestamp without time zone").HasColumnName("requested_at");
            entity.Property(e => e.ReviewerId).HasColumnName("reviewer_id");
            entity.Property(e => e.ReviewedAt).HasColumnType("timestamp without time zone").HasColumnName("reviewed_at");
            entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
            entity.Property(e => e.ReversalOperationId).HasColumnName("reversal_operation_id");
            entity.Property(e => e.ReplacementOperationId).HasColumnName("replacement_operation_id");
            entity.HasOne(e => e.Operation).WithMany()
                .HasForeignKey(e => e.OperationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("operation_correction_proposals_operation_id_fkey");
        });

        modelBuilder.Entity<ShopifyOrderLink>(entity =>
        {
            entity.HasKey(e => e.OperationId).HasName("shopify_order_links_pkey");
            entity.ToTable("shopify_order_links", "operations");
            entity.HasIndex(e => e.ShopifyOrderId, "uq_shopify_order_links_order").IsUnique();
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.ShopifyOrderId).HasMaxLength(100).HasColumnName("shopify_order_id");
            entity.Property(e => e.ShopifyOrderNumber).HasMaxLength(100).HasColumnName("shopify_order_number");
            entity.Property(e => e.PaymentReference).HasMaxLength(255).HasColumnName("payment_reference");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
            entity.HasOne(e => e.Operation).WithOne(e => e.ShopifyOrderLink)
                .HasForeignKey<ShopifyOrderLink>(e => e.OperationId).OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("shopify_order_links_operation_id_fkey");
        });

        modelBuilder.Entity<ShopifyWebhookEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("shopify_webhook_events_pkey");
            entity.ToTable("shopify_webhook_events", "operations");
            entity.HasIndex(e => e.WebhookId, "uq_shopify_webhook_events_webhook").IsUnique();
            entity.HasIndex(e => e.ShopifyOrderId, "idx_shopify_webhook_events_order");
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt }, "idx_shopify_webhook_events_ready");
            entity.Property(e => e.Id).HasDefaultValueSql("uuid_generate_v4()").HasColumnName("id");
            entity.Property(e => e.WebhookId).HasMaxLength(100).HasColumnName("webhook_id");
            entity.Property(e => e.Topic).HasMaxLength(100).HasColumnName("topic");
            entity.Property(e => e.ShopDomain).HasMaxLength(255).HasColumnName("shop_domain");
            entity.Property(e => e.VerificationMode).HasMaxLength(30).HasDefaultValue("Hmac").HasColumnName("verification_mode");
            entity.Property(e => e.EventId).HasMaxLength(100).HasColumnName("event_id");
            entity.Property(e => e.ApiVersion).HasMaxLength(30).HasColumnName("api_version");
            entity.Property(e => e.PayloadHash).HasMaxLength(128).HasColumnName("payload_hash");
            entity.Property(e => e.ProtectedPayload).HasColumnName("protected_payload");
            entity.Property(e => e.Status).HasMaxLength(50).HasColumnName("status");
            entity.Property(e => e.Detail).HasColumnName("detail");
            entity.Property(e => e.ShopifyOrderId).HasMaxLength(100).HasColumnName("shopify_order_id");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.ReceivedAt).HasColumnType("timestamp without time zone").HasColumnName("received_at");
            entity.Property(e => e.VerifiedAt).HasColumnType("timestamp without time zone").HasColumnName("verified_at");
            entity.Property(e => e.TriggeredAt).HasColumnType("timestamp without time zone").HasColumnName("triggered_at");
            entity.Property(e => e.ProcessedAt).HasColumnType("timestamp without time zone").HasColumnName("processed_at");
            entity.Property(e => e.NextAttemptAt).HasColumnType("timestamp without time zone").HasColumnName("next_attempt_at");
            entity.Property(e => e.LeaseUntil).HasColumnType("timestamp without time zone").HasColumnName("lease_until");
            entity.Property(e => e.AttemptCount).HasDefaultValue(0).HasColumnName("attempt_count");
            entity.Property(e => e.ResolvedAt).HasColumnType("timestamp without time zone").HasColumnName("resolved_at");
            entity.Property(e => e.ResolvedBy).HasColumnName("resolved_by");
            entity.Property(e => e.ResolutionNote).HasMaxLength(1000).HasColumnName("resolution_note");
        });

        modelBuilder.Entity<OperationVersion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("operation_versions_pkey");

            entity.ToTable("operation_versions", "operations");

            entity.HasIndex(e => e.OperationId, "idx_op_versions_operation");

            entity.HasIndex(e => new { e.OperationId, e.VersionNumber }, "uq_op_version").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.EditedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("edited_at");
            entity.Property(e => e.EditedBy).HasColumnName("edited_by");
            entity.Property(e => e.EditedActorName).HasMaxLength(100).HasColumnName("edited_actor_name");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.Reason)
                .HasDefaultValueSql("'Initial'::text")
                .HasColumnName("reason");
            entity.Property(e => e.SnapshotData)
                .HasColumnType("jsonb")
                .HasColumnName("snapshot_data");
            entity.Property(e => e.VersionNumber).HasColumnName("version_number");

            entity.HasOne(d => d.Operation).WithMany(p => p.OperationVersions)
                .HasForeignKey(d => d.OperationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("operation_versions_operation_id_fkey");
        });

        modelBuilder.Entity<StocktakeAdjustmentLine>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("stocktake_adjustment_lines_pkey");

            entity.ToTable("stocktake_adjustment_lines", "operations");

            entity.HasIndex(e => e.SessionId, "idx_stocktake_adj_session");

            entity.HasIndex(e => new { e.SessionId, e.SkuId, e.LotNumber, e.ExpiryDate }, "uq_stocktake_line_batch")
                .IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Delta).HasColumnName("delta");
            entity.Property(e => e.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(e => e.LineNote).HasColumnName("line_note");
            entity.Property(e => e.LotNumber)
                .HasMaxLength(100)
                .HasColumnName("lot_number");
            entity.Property(e => e.PhysicalCount).HasColumnName("physical_count");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.SkuId).HasColumnName("sku_id");
            entity.Property(e => e.SystemQtyBefore).HasColumnName("system_qty_before");
            entity.Property(e => e.BaselineStockRowVersion).HasColumnName("baseline_stock_row_version");

            entity.HasOne(d => d.Session).WithMany(p => p.StocktakeAdjustmentLines)
                .HasForeignKey(d => d.SessionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("stocktake_adjustment_lines_session_id_fkey");
        });

        modelBuilder.Entity<StocktakeSession>(entity =>
        {
            entity.Property(e => e.ConcurrencyVersion).HasColumnName("xmin").IsRowVersion();
            entity.HasKey(e => e.Id).HasName("stocktake_sessions_pkey");

            entity.ToTable("stocktake_sessions", "operations", table =>
            {
                table.HasCheckConstraint("chk_stocktake_status", "status in ('Draft','Confirmed')");
            });

            entity.HasIndex(e => e.LocationId, "idx_stocktake_location");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ConfirmedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("confirmed_at");
            entity.Property(e => e.ConfirmedBy).HasColumnName("confirmed_by");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.PerformedBy).HasColumnName("performed_by");
            entity.Property(e => e.ProductsCounted).HasColumnName("products_counted");
            entity.Property(e => e.SessionDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("session_date");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Draft'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TotalDiscrepancyUnits).HasColumnName("total_discrepancy_units");
        });

        modelBuilder.Entity<SupplyShipment>(entity =>
        {
            entity.Property(e => e.ConcurrencyVersion).HasColumnName("xmin").IsRowVersion();
            entity.HasKey(e => e.Id).HasName("supply_shipments_pkey");

            entity.ToTable("supply_shipments", "operations", table =>
            {
                table.HasCheckConstraint("chk_supply_shipments_status", "status in ('Draft','Received','Cancelled')");
                table.HasCheckConstraint("chk_supply_shipments_product_subtotal", "product_subtotal >= 0");
                table.HasCheckConstraint("chk_supply_shipments_cost_subtotal", "cost_subtotal >= 0");
                table.HasCheckConstraint("chk_supply_shipments_landed_total", "landed_total >= 0");
            });

            entity.HasIndex(e => e.CreatedAt, "idx_supply_shipments_created_at").IsDescending();
            entity.HasIndex(e => e.DestinationLocationId, "idx_supply_shipments_destination");
            entity.HasIndex(e => e.InventoryReceiptOperationId, "uq_supply_shipments_operation")
                .IsUnique()
                .HasFilter("(inventory_receipt_operation_id IS NOT NULL)");
            entity.HasIndex(e => e.ShipmentNumber, "supply_shipments_shipment_number_key").IsUnique();
            entity.HasIndex(e => e.Status, "idx_supply_shipments_status");

            entity.Property(e => e.Id).HasDefaultValueSql("uuid_generate_v4()").HasColumnName("id");
            entity.Property(e => e.CancelledAt).HasColumnType("timestamp without time zone").HasColumnName("cancelled_at");
            entity.Property(e => e.CancelledBy).HasColumnName("cancelled_by");
            entity.Property(e => e.ConfirmedAt).HasColumnType("timestamp without time zone").HasColumnName("confirmed_at");
            entity.Property(e => e.ConfirmedBy).HasColumnName("confirmed_by");
            entity.Property(e => e.CostSubtotal).HasPrecision(18, 4).HasColumnName("cost_subtotal");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.DestinationLocationId).HasColumnName("destination_location_id");
            entity.Property(e => e.InventoryReceiptOperationId).HasColumnName("inventory_receipt_operation_id");
            entity.Property(e => e.InvoiceNumber).HasMaxLength(100).HasColumnName("invoice_number");
            entity.Property(e => e.LandedTotal).HasPrecision(18, 4).HasColumnName("landed_total");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.ProductSubtotal).HasPrecision(18, 4).HasColumnName("product_subtotal");
            entity.Property(e => e.ShipmentDate).HasColumnType("timestamp without time zone").HasColumnName("shipment_date");
            entity.Property(e => e.ShipmentNumber).HasMaxLength(50).HasColumnName("shipment_number");
            entity.Property(e => e.Status).HasMaxLength(50).HasDefaultValueSql("'Draft'::character varying").HasColumnName("status");
            entity.Property(e => e.SupplierName).HasMaxLength(255).HasColumnName("supplier_name");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.InventoryReceiptOperation).WithMany()
                .HasForeignKey(d => d.InventoryReceiptOperationId)
                .HasConstraintName("supply_shipments_inventory_receipt_operation_id_fkey");
        });

        modelBuilder.Entity<SupplyShipmentLine>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("supply_shipment_lines_pkey");

            entity.ToTable("supply_shipment_lines", "operations", table =>
            {
                table.HasCheckConstraint("chk_supply_lines_quantity", "quantity > 0");
                table.HasCheckConstraint("chk_supply_lines_unit_price", "unit_price is null or unit_price >= 0");
                table.HasCheckConstraint("chk_supply_lines_line_subtotal", "line_subtotal >= 0");
                table.HasCheckConstraint("chk_supply_lines_allocated_cost", "allocated_cost >= 0");
                table.HasCheckConstraint("chk_supply_lines_landed_unit_cost", "landed_unit_cost >= 0");
            });

            entity.HasIndex(e => e.ShipmentId, "idx_supply_lines_shipment");
            entity.HasIndex(e => e.SkuId, "idx_supply_lines_sku");

            entity.Property(e => e.Id).HasDefaultValueSql("uuid_generate_v4()").HasColumnName("id");
            entity.Property(e => e.AllocatedCost).HasPrecision(18, 4).HasColumnName("allocated_cost");
            entity.Property(e => e.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(e => e.LandedUnitCost).HasPrecision(18, 4).HasColumnName("landed_unit_cost");
            entity.Property(e => e.LineSubtotal).HasPrecision(18, 4).HasColumnName("line_subtotal");
            entity.Property(e => e.LotNumber).HasMaxLength(100).HasColumnName("lot_number");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.ProductNameSnapshot).HasMaxLength(255).HasColumnName("product_name_snapshot");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.ShipmentId).HasColumnName("shipment_id");
            entity.Property(e => e.SkuCodeSnapshot).HasMaxLength(100).HasColumnName("sku_code_snapshot");
            entity.Property(e => e.SkuId).HasColumnName("sku_id");
            entity.Property(e => e.UnitPrice).HasPrecision(18, 4).HasColumnName("unit_price");

            entity.HasOne(d => d.Shipment).WithMany(p => p.Lines)
                .HasForeignKey(d => d.ShipmentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("supply_shipment_lines_shipment_id_fkey");
        });

        modelBuilder.Entity<SupplyShipmentCost>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("supply_shipment_costs_pkey");

            entity.ToTable("supply_shipment_costs", "operations", table =>
            {
                table.HasCheckConstraint("chk_supply_costs_amount", "amount >= 0");
            });

            entity.HasIndex(e => e.ShipmentId, "idx_supply_costs_shipment");

            entity.Property(e => e.Id).HasDefaultValueSql("uuid_generate_v4()").HasColumnName("id");
            entity.Property(e => e.Amount).HasPrecision(18, 4).HasColumnName("amount");
            entity.Property(e => e.CostType).HasMaxLength(50).HasColumnName("cost_type");
            entity.Property(e => e.Description).HasMaxLength(255).HasColumnName("description");
            entity.Property(e => e.ShipmentId).HasColumnName("shipment_id");

            entity.HasOne(d => d.Shipment).WithMany(p => p.Costs)
                .HasForeignKey(d => d.ShipmentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("supply_shipment_costs_shipment_id_fkey");
        });

        modelBuilder.Entity<SupplyShipmentHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("supply_shipment_history_pkey");

            entity.ToTable("supply_shipment_history", "operations");

            entity.HasIndex(e => new { e.ShipmentId, e.CreatedAt }, "idx_supply_history_shipment_created").IsDescending(false, true);

            entity.Property(e => e.Id).HasDefaultValueSql("uuid_generate_v4()").HasColumnName("id");
            entity.Property(e => e.Action).HasMaxLength(50).HasColumnName("action");
            entity.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.ShipmentId).HasColumnName("shipment_id");
            entity.Property(e => e.SnapshotData).HasColumnType("jsonb").HasColumnName("snapshot_data");
            entity.Property(e => e.Summary).HasColumnName("summary");

            entity.HasOne(d => d.Shipment).WithMany(p => p.HistoryLogs)
                .HasForeignKey(d => d.ShipmentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("supply_shipment_history_shipment_id_fkey");
        });

        modelBuilder.Entity<ReplenishmentRun>(entity =>
        {
            entity.HasKey(value => value.Id).HasName("replenishment_runs_pkey");
            entity.ToTable("replenishment_runs", "operations");
            entity.HasIndex(value => value.RunKey, "uq_replenishment_runs_run_key").IsUnique();
            entity.Property(value => value.Id).HasDefaultValueSql("uuid_generate_v4()").HasColumnName("id");
            entity.Property(value => value.RunKey).HasMaxLength(40).HasColumnName("run_key");
            entity.Property(value => value.CairoDate).HasColumnName("cairo_date");
            entity.Property(value => value.Trigger).HasMaxLength(20).HasColumnName("trigger");
            entity.Property(value => value.Status).HasMaxLength(20).HasColumnName("status");
            entity.Property(value => value.StartedAt).HasColumnType("timestamp without time zone").HasColumnName("started_at");
            entity.Property(value => value.CompletedAt).HasColumnType("timestamp without time zone").HasColumnName("completed_at");
            entity.Property(value => value.CreatedOperations).HasColumnName("created_operations");
            entity.Property(value => value.UncoveredQuantity).HasColumnName("uncovered_quantity");
        });
        modelBuilder.HasSequence("operation_number_seq", "operations").StartsAt(1000L);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
