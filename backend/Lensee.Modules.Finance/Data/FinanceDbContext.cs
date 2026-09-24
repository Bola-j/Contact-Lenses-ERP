using Microsoft.EntityFrameworkCore;

namespace Lensee.Modules.Finance.Data;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public DbSet<FinanceAccount> FinanceAccounts => Set<FinanceAccount>();
    public DbSet<FinanceLedgerEntry> FinanceLedgerEntries => Set<FinanceLedgerEntry>();
    public DbSet<FinanceOpeningBalance> FinanceOpeningBalances => Set<FinanceOpeningBalance>();
    public DbSet<PaymentMovementRegistry> PaymentMovementRegistries => Set<PaymentMovementRegistry>();
    public DbSet<FinanceExpense> FinanceExpenses => Set<FinanceExpense>();
    public DbSet<CLevelWithdrawal> CLevelWithdrawals => Set<CLevelWithdrawal>();
    public DbSet<CLevelWithdrawalRepayment> CLevelWithdrawalRepayments => Set<CLevelWithdrawalRepayment>();
    public DbSet<FinanceCategory> FinanceCategories => Set<FinanceCategory>();
    public DbSet<FinanceTransfer> FinanceTransfers => Set<FinanceTransfer>();
    public DbSet<ReconciliationImportPackage> ReconciliationImportPackages => Set<ReconciliationImportPackage>();
    public DbSet<ReconciliationImportRow> ReconciliationImportRows => Set<ReconciliationImportRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");
        modelBuilder.Entity<FinanceAccount>(entity =>
        {
            entity.ToTable("finance_accounts", "finance", table =>
            {
                table.HasCheckConstraint("chk_finance_account_type", "type in ('CashOnHand','BankAccount','Wallet')");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.Name, value.Type }).IsUnique();
            entity.Property(value => value.Id).HasColumnName("id").HasDefaultValueSql("uuid_generate_v4()");
            entity.Property(value => value.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            entity.Property(value => value.Type).HasColumnName("type").HasMaxLength(30).IsRequired();
            entity.Property(value => value.Reference).HasMaxLength(200);
            entity.Property(value => value.IsActive).HasColumnName("is_active");
            entity.Property(value => value.Reference).HasColumnName("reference");
            entity.Property(value => value.Details).HasColumnName("details");
            entity.Property(value => value.CreatedBy).HasColumnName("created_by");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp without time zone");
        });
        modelBuilder.Entity<FinanceLedgerEntry>(entity =>
        {
            entity.ToTable("finance_ledger_entries", "finance", table =>
            {
                table.HasCheckConstraint("chk_finance_ledger_direction", "direction in ('Debit','Credit')");
                table.HasCheckConstraint("chk_finance_ledger_amount", "amount > 0");
                table.HasCheckConstraint("chk_finance_ledger_status", "status in ('Posted','Reversed')");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.SourceType, value.SourceId }).IsUnique();
            entity.HasIndex(value => new { value.FinanceAccountId, value.BusinessDate });
            entity.HasIndex(value => value.ExternalReference).IsUnique().HasFilter("(external_reference is not null)");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.FinanceAccountId).HasColumnName("finance_account_id");
            entity.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 4);
            entity.Property(value => value.Direction).HasColumnName("direction").HasMaxLength(10).IsRequired();
            entity.Property(value => value.Category).HasColumnName("category").HasMaxLength(60).IsRequired();
            entity.Property(value => value.MovementMethod).HasColumnName("movement_method").HasMaxLength(50);
            entity.Property(value => value.SourceType).HasColumnName("source_type").HasMaxLength(80).IsRequired();
            entity.Property(value => value.SourceId).HasColumnName("source_id");
            entity.Property(value => value.BusinessDate).HasColumnName("business_date");
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("Posted");
            entity.Property(value => value.CreatedBy).HasColumnName("created_by");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.ReversesEntryId).HasColumnName("reverses_entry_id");
            entity.Property(value => value.CorrelationId).HasColumnName("correlation_id");
            entity.Property(value => value.ExternalReference).HasColumnName("external_reference").HasMaxLength(200);
            entity.HasOne(value => value.FinanceAccount).WithMany(value => value.Entries).HasForeignKey(value => value.FinanceAccountId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<FinanceOpeningBalance>(entity =>
        {
            entity.ToTable("finance_opening_balances", "finance", table =>
            {
                table.HasCheckConstraint("chk_finance_opening_amount", "amount > 0");
                table.HasCheckConstraint("chk_finance_opening_direction", "direction in ('Debit','Credit')");
                table.HasCheckConstraint("chk_finance_opening_status", "status in ('Draft','PendingReview','Posted','Rejected','Corrected')");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.PostedEntryId).IsUnique().HasFilter("(posted_entry_id is not null)");
            entity.HasIndex(value => value.FinanceAccountId).IsUnique()
                .HasDatabaseName("ux_finance_opening_one_root_per_account")
                .HasFilter("(reverses_opening_balance_id is null and status <> 'Rejected')");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.FinanceAccountId).HasColumnName("finance_account_id");
            entity.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 4);
            entity.Property(value => value.Direction).HasColumnName("direction").HasMaxLength(10).HasDefaultValue("Credit");
            entity.Property(value => value.AsOfDate).HasColumnName("as_of_date");
            entity.Property(value => value.Description).HasColumnName("description");
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(30).HasDefaultValue("Draft");
            entity.Property(value => value.CreatedBy).HasColumnName("created_by");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.ReviewedBy).HasColumnName("reviewed_by");
            entity.Property(value => value.ReviewedAt).HasColumnName("reviewed_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.PostedEntryId).HasColumnName("posted_entry_id");
            entity.Property(value => value.ReversesOpeningBalanceId).HasColumnName("reverses_opening_balance_id");
            entity.Property(value => value.ReplacedByOpeningBalanceId).HasColumnName("replaced_by_opening_balance_id");
            entity.Property(value => value.CorrelationId).HasColumnName("correlation_id");
            entity.HasOne(value => value.FinanceAccount).WithMany().HasForeignKey(value => value.FinanceAccountId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PaymentMovementRegistry>(entity =>
        {
            entity.ToTable("payment_movement_registry", "finance", table =>
            {
                table.HasCheckConstraint("chk_payment_movement_amount", "amount > 0");
                table.HasCheckConstraint("chk_payment_movement_status", "status in ('Posted','Reversed')");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.SourceType, value.SourceId }).IsUnique();
            entity.HasIndex(value => value.NormalizedExternalReference).IsUnique().HasFilter("(normalized_external_reference is not null)");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.SourceType).HasColumnName("source_type");
            entity.Property(value => value.SourceId).HasColumnName("source_id");
            entity.Property(value => value.Track).HasColumnName("track").HasMaxLength(30).IsRequired();
            entity.Property(value => value.MovementMethod).HasColumnName("movement_method").HasMaxLength(50).IsRequired();
            entity.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 4);
            entity.Property(value => value.FinanceAccountId).HasColumnName("finance_account_id");
            entity.Property(value => value.NormalizedExternalReference).HasColumnName("normalized_external_reference").HasMaxLength(200);
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("Posted");
            entity.Property(value => value.CreatedBy).HasColumnName("created_by");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.CorrelationId).HasColumnName("correlation_id");
        });
        modelBuilder.Entity<FinanceExpense>(entity =>
        {
            entity.ToTable("finance_expenses", "finance", table =>
            {
                table.HasCheckConstraint("chk_finance_expense_amount", "amount > 0");
                table.HasCheckConstraint("chk_finance_expense_status", "status in ('Draft','PendingReview','Posted','Rejected','Corrected','Pending','Paid')");
                table.HasCheckConstraint("chk_finance_expense_other_description", "category <> 'Other' or length(trim(coalesce(description, ''))) > 0");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.PostedFinanceLedgerEntryId).IsUnique().HasFilter("(posted_finance_ledger_entry_id is not null)");
            entity.HasIndex(value => value.ReplacedByExpenseId).IsUnique().HasFilter("(replaced_by_expense_id is not null)");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.FinanceAccountId).HasColumnName("finance_account_id");
            entity.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 4);
            entity.Property(value => value.Category).HasColumnName("category").HasMaxLength(60).IsRequired();
            entity.Property(value => value.CategoryId).HasColumnName("category_id");
            entity.Property(value => value.CorrectionNote).HasColumnName("correction_note").HasMaxLength(1000);
            entity.Property(value => value.TransferId).HasColumnName("transfer_id");
            entity.Property(value => value.MovementMethod).HasColumnName("movement_method").HasMaxLength(50).IsRequired();
            entity.Property(value => value.BusinessDate).HasColumnName("business_date");
            entity.Property(value => value.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(30).HasDefaultValue("Draft");
            entity.Property(value => value.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.ApprovedByUserId).HasColumnName("approved_by_user_id");
            entity.Property(value => value.ApprovedAt).HasColumnName("approved_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.PostedFinanceLedgerEntryId).HasColumnName("posted_finance_ledger_entry_id");
            entity.Property(value => value.ReversesExpenseId).HasColumnName("reverses_expense_id");
            entity.Property(value => value.ReplacedByExpenseId).HasColumnName("replaced_by_expense_id");
            entity.Property(value => value.ExternalReference).HasColumnName("external_reference").HasMaxLength(200);
            entity.Property(value => value.CorrelationId).HasColumnName("correlation_id").HasMaxLength(100);
            entity.HasOne(value => value.FinanceAccount).WithMany().HasForeignKey(value => value.FinanceAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FinanceCategory>().WithMany().HasForeignKey(value => value.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CLevelWithdrawal>(entity =>
        {
            entity.ToTable("c_level_withdrawals", "finance", table =>
            {
                table.HasCheckConstraint("chk_c_level_withdrawal_amount", "amount > 0");
                table.HasCheckConstraint("chk_c_level_withdrawal_status", "status in ('Draft','PendingReview','Posted','Rejected','Corrected')");
                table.HasCheckConstraint("chk_c_level_withdrawal_reason", "length(trim(reason)) > 0");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.PostedFinanceLedgerEntryId).IsUnique().HasFilter("(posted_finance_ledger_entry_id is not null)");
            entity.HasIndex(value => value.ReplacedByWithdrawalId).IsUnique().HasFilter("(replaced_by_withdrawal_id is not null)");
            entity.HasIndex(value => new { value.AssignedToCLevelUserId, value.Status });
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.AssignedToCLevelUserId).HasColumnName("assigned_to_c_level_user_id");
            entity.Property(value => value.AssignedByUserId).HasColumnName("assigned_by_user_id");
            entity.Property(value => value.FinanceAccountId).HasColumnName("finance_account_id");
            entity.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 4);
            entity.Property(value => value.MovementMethod).HasColumnName("movement_method").HasMaxLength(50).IsRequired();
            entity.Property(value => value.BusinessDate).HasColumnName("business_date");
            entity.Property(value => value.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
            entity.Property(value => value.CategoryId).HasColumnName("category_id");
            entity.Property(value => value.CorrectionNote).HasColumnName("correction_note").HasMaxLength(1000);
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(30).HasDefaultValue("Draft");
            entity.Property(value => value.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.ApprovedByUserId).HasColumnName("approved_by_user_id");
            entity.Property(value => value.ApprovedAt).HasColumnName("approved_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.PostedFinanceLedgerEntryId).HasColumnName("posted_finance_ledger_entry_id");
            entity.Property(value => value.ReversesWithdrawalId).HasColumnName("reverses_withdrawal_id");
            entity.Property(value => value.ReplacedByWithdrawalId).HasColumnName("replaced_by_withdrawal_id");
            entity.Property(value => value.ExternalReference).HasColumnName("external_reference").HasMaxLength(200);
            entity.Property(value => value.CorrelationId).HasColumnName("correlation_id").HasMaxLength(100);
            entity.HasOne(value => value.FinanceAccount).WithMany().HasForeignKey(value => value.FinanceAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FinanceCategory>().WithMany().HasForeignKey(value => value.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<FinanceCategory>(entity =>
        {
            entity.ToTable("finance_categories", "finance", table =>
            {
                table.HasCheckConstraint("chk_finance_category_kind", "kind in ('Expense','Withdrawal')");
                table.HasCheckConstraint("chk_finance_category_order", "sort_order >= 0");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.Kind, value.Code }).IsUnique();
            entity.HasIndex(value => new { value.Kind, value.SortOrder });
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
            entity.Property(value => value.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            entity.Property(value => value.EnglishName).HasColumnName("english_name").HasMaxLength(150).IsRequired();
            entity.Property(value => value.ArabicName).HasColumnName("arabic_name").HasMaxLength(150).IsRequired();
            entity.Property(value => value.ParentId).HasColumnName("parent_id");
            entity.Property(value => value.SortOrder).HasColumnName("sort_order");
            entity.Property(value => value.IsActive).HasColumnName("is_active");
            entity.Property(value => value.CreatedBy).HasColumnName("created_by");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp without time zone");
            entity.HasOne<FinanceCategory>().WithMany().HasForeignKey(value => value.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<FinanceTransfer>(entity =>
        {
            entity.ToTable("finance_transfers", "finance", table =>
            {
                table.HasCheckConstraint("chk_finance_transfer_amount", "amount > 0 and fee_amount >= 0");
                table.HasCheckConstraint("chk_finance_transfer_accounts", "source_account_id <> destination_account_id");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.ClientRequestId).IsUnique();
            entity.HasIndex(value => value.ExternalReference).IsUnique().HasFilter("(external_reference is not null)");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.ClientRequestId).HasColumnName("client_request_id");
            entity.Property(value => value.SourceAccountId).HasColumnName("source_account_id");
            entity.Property(value => value.DestinationAccountId).HasColumnName("destination_account_id");
            entity.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 4);
            entity.Property(value => value.FeeAmount).HasColumnName("fee_amount").HasPrecision(18, 4);
            entity.Property(value => value.BusinessDate).HasColumnName("business_date");
            entity.Property(value => value.Notes).HasColumnName("notes").HasMaxLength(1000);
            entity.Property(value => value.ExternalReference).HasColumnName("external_reference").HasMaxLength(200);
            entity.Property(value => value.CreatedBy).HasColumnName("created_by");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.SourceEntryId).HasColumnName("source_entry_id");
            entity.Property(value => value.DestinationEntryId).HasColumnName("destination_entry_id");
            entity.Property(value => value.FeeExpenseId).HasColumnName("fee_expense_id");
            entity.HasOne<FinanceAccount>().WithMany().HasForeignKey(value => value.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FinanceAccount>().WithMany().HasForeignKey(value => value.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CLevelWithdrawalRepayment>(entity =>
        {
            entity.ToTable("c_level_withdrawal_repayments", "finance", table => table.HasCheckConstraint("chk_withdrawal_repayment_amount", "amount > 0"));
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.ClientRequestId).IsUnique();
            entity.HasIndex(value => value.ExternalReference).IsUnique().HasFilter("(external_reference is not null)");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.ClientRequestId).HasColumnName("client_request_id");
            entity.Property(value => value.WithdrawalId).HasColumnName("withdrawal_id");
            entity.Property(value => value.FinanceAccountId).HasColumnName("finance_account_id");
            entity.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 4);
            entity.Property(value => value.MovementMethod).HasColumnName("movement_method").HasMaxLength(50).IsRequired();
            entity.Property(value => value.BusinessDate).HasColumnName("business_date");
            entity.Property(value => value.Notes).HasColumnName("notes").HasMaxLength(1000);
            entity.Property(value => value.ExternalReference).HasColumnName("external_reference").HasMaxLength(200);
            entity.Property(value => value.CreatedBy).HasColumnName("created_by");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.PostedEntryId).HasColumnName("posted_entry_id");
            entity.HasOne<CLevelWithdrawal>().WithMany().HasForeignKey(value => value.WithdrawalId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FinanceAccount>().WithMany().HasForeignKey(value => value.FinanceAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(value => value.ReversesRepaymentId).HasColumnName("reverses_repayment_id");
            entity.Property(value => value.ReplacedByRepaymentId).HasColumnName("replaced_by_repayment_id");
            entity.Property(value => value.CorrectionNote).HasColumnName("correction_note").HasMaxLength(1000);
            entity.HasIndex(value => value.ReversesRepaymentId).IsUnique().HasFilter("(reverses_repayment_id is not null)");
            entity.HasOne<CLevelWithdrawalRepayment>().WithMany().HasForeignKey(value => value.ReversesRepaymentId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ReconciliationImportPackage>(entity =>
        {
            entity.ToTable("reconciliation_import_packages", "finance", table =>
            {
                table.HasCheckConstraint("chk_reconciliation_package_status", "status in ('Registered','Previewed','Applied','Rejected')");
                table.HasCheckConstraint("chk_reconciliation_package_period", "source_period_start <= source_period_end");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.PayloadSha256).IsUnique();
            entity.HasIndex(value => value.ApplyIdempotencyKey).IsUnique().HasFilter("(apply_idempotency_key is not null)");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.SchemaVersion).HasColumnName("schema_version").HasMaxLength(30).IsRequired();
            entity.Property(value => value.Signer).HasColumnName("signer").HasMaxLength(200).IsRequired();
            entity.Property(value => value.Signature).HasColumnName("signature").HasMaxLength(1000).IsRequired();
            entity.Property(value => value.PayloadSha256).HasColumnName("payload_sha256").HasMaxLength(64).IsRequired();
            entity.Property(value => value.SourcePeriodStart).HasColumnName("source_period_start");
            entity.Property(value => value.SourcePeriodEnd).HasColumnName("source_period_end");
            entity.Property(value => value.ExportedAtUtc).HasColumnName("exported_at_utc").HasColumnType("timestamp without time zone");
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(30).HasDefaultValue("Registered");
            entity.Property(value => value.RegisteredByUserId).HasColumnName("registered_by_user_id");
            entity.Property(value => value.RegisteredAt).HasColumnName("registered_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.AppliedByUserId).HasColumnName("applied_by_user_id");
            entity.Property(value => value.AppliedAt).HasColumnName("applied_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.ApplyIdempotencyKey).HasColumnName("apply_idempotency_key").HasMaxLength(200);
        });
        modelBuilder.Entity<ReconciliationImportRow>(entity =>
        {
            entity.ToTable("reconciliation_import_rows", "finance", table =>
            {
                table.HasCheckConstraint("chk_reconciliation_row_decision", "decision in ('Approved','PendingReview','Rejected','Applied')");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.PackageId, value.RowNumber }).IsUnique();
            entity.HasIndex(value => new { value.PackageId, value.SourceReference }).IsUnique();
            entity.HasIndex(value => new { value.PackageId, value.Decision });
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.PackageId).HasColumnName("package_id");
            entity.Property(value => value.RowNumber).HasColumnName("row_number");
            entity.Property(value => value.RowType).HasColumnName("row_type").HasMaxLength(50).IsRequired();
            entity.Property(value => value.SourceReference).HasColumnName("source_reference").HasMaxLength(200).IsRequired();
            entity.Property(value => value.OriginalPayload).HasColumnName("original_payload").HasColumnType("jsonb").IsRequired();
            entity.Property(value => value.Decision).HasColumnName("decision").HasMaxLength(30).HasDefaultValue("PendingReview");
            entity.Property(value => value.CanonicalTrack).HasColumnName("canonical_track").HasMaxLength(30);
            entity.Property(value => value.DecisionReason).HasColumnName("decision_reason").HasMaxLength(1000);
            entity.Property(value => value.ResolvedByUserId).HasColumnName("resolved_by_user_id");
            entity.Property(value => value.ResolvedAt).HasColumnName("resolved_at").HasColumnType("timestamp without time zone");
            entity.Property(value => value.ResolutionNote).HasColumnName("resolution_note").HasMaxLength(1000);
            entity.HasOne(value => value.Package).WithMany(value => value.Rows).HasForeignKey(value => value.PackageId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
