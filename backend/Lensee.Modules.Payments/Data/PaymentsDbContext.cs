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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<CashRecord>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("cash_records_pkey");

            entity.ToTable("cash_records", "payments", table =>
            {
                table.HasCheckConstraint("chk_cash_payment_type", "payment_type in ('CashReceived','CashRefund')");
                table.HasCheckConstraint("chk_cash_status", "status in ('PendingAccountant','Completed','Cancelled')");
                table.HasCheckConstraint("chk_cash_amount", "amount > 0");
            });

            entity.HasIndex(e => e.PaymentDate, "idx_cash_records_date").IsDescending();

            entity.HasIndex(e => e.OperationId, "idx_cash_records_operation");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 4)
                .HasColumnName("amount");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
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
            entity.Property(e => e.MainLogId).HasColumnName("main_log_id");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(50)
                .HasColumnName("payment_method");
            entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
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
                table.HasCheckConstraint("chk_financial_adjustment_type", "adjustment_type in ('MerchantCredit','BalanceReduction','CashRefund')");
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
                table.HasCheckConstraint("chk_main_payment_method", "payment_method in ('CashHandToHand','CashTransaction','Installment')");
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
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Installment'::character varying")
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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
