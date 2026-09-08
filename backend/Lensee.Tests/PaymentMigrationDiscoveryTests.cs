using System.Reflection;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
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
