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

    public virtual DbSet<OperationLine> OperationLines { get; set; }

    public virtual DbSet<OperationLog> OperationLogs { get; set; }

    public virtual DbSet<OperationVersion> OperationVersions { get; set; }

    public virtual DbSet<StocktakeAdjustmentLine> StocktakeAdjustmentLines { get; set; }

    public virtual DbSet<StocktakeSession> StocktakeSessions { get; set; }

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
            });

            entity.HasIndex(e => e.ClientId, "idx_op_logs_client").HasFilter("(client_id IS NOT NULL)");

            entity.HasIndex(e => e.CreatedAt, "idx_op_logs_created_at").IsDescending();

            entity.HasIndex(e => e.CreatedBy, "idx_op_logs_created_by");

            entity.HasIndex(e => e.SourceLocationId, "idx_op_logs_source_location");

            entity.HasIndex(e => new { e.OperationType, e.Status }, "idx_op_logs_type_status");

            entity.HasIndex(e => e.OperationNumber, "operation_logs_operation_number_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ClientId).HasColumnName("client_id");
            entity.Property(e => e.ClientName)
                .HasMaxLength(255)
                .HasColumnName("client_name");
            entity.Property(e => e.ConfirmedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("confirmed_at");
            entity.Property(e => e.ConfirmedBy).HasColumnName("confirmed_by");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
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
            entity.Property(e => e.RepresentativeId).HasColumnName("representative_id");
            entity.Property(e => e.SourceLocationId).HasColumnName("source_location_id");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Draft'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.CurrentVersion).WithMany(p => p.OperationLogs)
                .HasForeignKey(d => d.CurrentVersionId)
                .HasConstraintName("fk_current_version");
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

            entity.HasOne(d => d.Session).WithMany(p => p.StocktakeAdjustmentLines)
                .HasForeignKey(d => d.SessionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("stocktake_adjustment_lines_session_id_fkey");
        });

        modelBuilder.Entity<StocktakeSession>(entity =>
        {
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
        modelBuilder.HasSequence("operation_number_seq", "operations").StartsAt(1000L);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
