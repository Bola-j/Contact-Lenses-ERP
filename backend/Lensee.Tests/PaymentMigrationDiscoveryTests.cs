using System.Reflection;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Payments.Migrations;
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
}
