using System.Reflection;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Finance.Data;
using Lensee.Modules.Finance.Migrations;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Payments.Migrations;
using Lensee.Modules.Reporting.Data;
using Lensee.SharedKernel.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

public sealed class PaymentMigrationDiscoveryTests
{
    [Fact]
    public void AdditionalChargeReclassificationMigration_IsDiscoverableByPaymentsContext()
    {
        var migrationType = typeof(ReclassifyMerchantCreditAsAdditionalCharge);

        var context = migrationType.GetCustomAttribute<DbContextAttribute>();
        var migration = migrationType.GetCustomAttribute<MigrationAttribute>();

        Assert.Equal(typeof(PaymentsDbContext), context?.ContextType);
        Assert.Equal("20260909090000_ReclassifyMerchantCreditAsAdditionalCharge", migration?.Id);
    }

    [Fact]
    public void PendingFinancialClosureUniquenessMigration_IsDiscoverableAndModelEnforcesPendingOperationUniqueness()
    {
        var migrationType = typeof(EnforceOnePendingFinancialClosurePerOperation);

        Assert.Equal(typeof(PaymentsDbContext), migrationType.GetCustomAttribute<DbContextAttribute>()?.ContextType);
        Assert.Equal("20260928130000_EnforceOnePendingFinancialClosurePerOperation", migrationType.GetCustomAttribute<MigrationAttribute>()?.Id);

        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused")
            .Options;
        using var context = new PaymentsDbContext(options);
        var index = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MerchantFinancialClosureItem))!
            .GetIndexes()
            .Single(value => value.Properties.Select(property => property.Name).SequenceEqual([nameof(MerchantFinancialClosureItem.OperationId)]) && value.GetFilter() == "(decision = 'Pending')");

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void FinancialClosureProperties_MapToExistingSnakeCaseColumns()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused")
            .Options;
        using var context = new PaymentsDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var item = model.FindEntityType(typeof(MerchantFinancialClosureItem))!;
        var proposal = model.FindEntityType(typeof(MerchantFinancialClosureProposal))!;

        Assert.Equal("operation_id", item.FindProperty(nameof(MerchantFinancialClosureItem.OperationId))!.GetColumnName());
        Assert.Equal("proposal_id", item.FindProperty(nameof(MerchantFinancialClosureItem.ProposalId))!.GetColumnName());
        Assert.Equal("account_id", proposal.FindProperty(nameof(MerchantFinancialClosureProposal.AccountId))!.GetColumnName());
    }

    [Fact]
    public void PostedSupplyInstallmentStatusMigration_IsDiscoverableByFinanceContext()
    {
        var migrationType = typeof(AllowPostedSupplyInstallmentStatus);

        Assert.Equal(typeof(FinanceDbContext), migrationType.GetCustomAttribute<DbContextAttribute>()?.ContextType);
        Assert.Equal("20260927110000_AllowPostedSupplyInstallmentStatus", migrationType.GetCustomAttribute<MigrationAttribute>()?.Id);

        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused")
            .Options;
        using var context = new FinanceDbContext(options);
        var constraint = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(SupplySupplierInstallment))!
            .GetCheckConstraints()
            .Single(value => value.Name == "chk_supply_installment_status");

        Assert.Contains("'Posted'", constraint.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("'Paid'", constraint.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCurrentMigration_HasDiscoveryMetadataForItsDbContext()
    {
        var contextAssemblies = new[]
        {
            typeof(SharedDbContext).Assembly,
            typeof(IdentityDbContext).Assembly,
            typeof(CatalogDbContext).Assembly,
            typeof(InventoryDbContext).Assembly,
            typeof(CrmDbContext).Assembly,
            typeof(OperationsDbContext).Assembly,
            typeof(PaymentsDbContext).Assembly,
            typeof(FinanceDbContext).Assembly,
            typeof(NotificationsDbContext).Assembly,
            typeof(ReportingDbContext).Assembly
        }.Distinct();

        var migrations = contextAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type));

        foreach (var migrationType in migrations)
        {
            var context = migrationType.GetCustomAttribute<DbContextAttribute>();
            var migration = migrationType.GetCustomAttribute<MigrationAttribute>();

            Assert.NotNull(context);
            Assert.NotNull(migration);
            Assert.Contains(context!.ContextType.Assembly, contextAssemblies);
        }
    }
}
