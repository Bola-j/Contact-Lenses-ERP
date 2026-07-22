using Lensee.SharedKernel.Security;
using Xunit;

namespace Lensee.Tests;

public sealed class PermissionTests
{
    [Fact]
    public void Admin_HasUserWritePermission()
    {
        var permissions = LenseePermissions.ForRole(LenseeRoles.Admin);

        Assert.Contains(LenseePermissions.UsersWrite, permissions);
        Assert.Contains(LenseePermissions.UsersPasswordWrite, permissions);
        Assert.Contains(LenseePermissions.SupplyWrite, permissions);
    }

    [Fact]
    public void ErpAdmin_MatchesAdminExceptSupplyAndPasswords()
    {
        var permissions = LenseePermissions.ForRole(LenseeRoles.ERPAdmin);

        Assert.Contains(LenseePermissions.UsersWrite, permissions);
        Assert.Contains(LenseePermissions.InventoryWrite, permissions);
        Assert.Contains(LenseePermissions.PaymentsWrite, permissions);
        Assert.DoesNotContain(LenseePermissions.UsersPasswordWrite, permissions);
        Assert.DoesNotContain(LenseePermissions.SupplyRead, permissions);
        Assert.DoesNotContain(LenseePermissions.SupplyWrite, permissions);
    }

    [Fact]
    public void CLevel_CanReadSupplyButCannotWriteSupply()
    {
        var permissions = LenseePermissions.ForRole(LenseeRoles.CLevel);

        Assert.Contains(LenseePermissions.SupplyRead, permissions);
        Assert.DoesNotContain(LenseePermissions.SupplyWrite, permissions);
    }

    [Fact]
    public void WarehouseClerk_DoesNotHavePaymentWritePermission()
    {
        var permissions = LenseePermissions.ForRole(LenseeRoles.WarehouseClerk);

        Assert.DoesNotContain(LenseePermissions.PaymentsWrite, permissions);
    }

    [Fact]
    public void CLevel_DoesNotHaveUserManagementPermissions()
    {
        var permissions = LenseePermissions.ForRole(LenseeRoles.CLevel);

        Assert.DoesNotContain(LenseePermissions.UsersRead, permissions);
        Assert.DoesNotContain(LenseePermissions.UsersWrite, permissions);
    }

    [Fact]
    public void Accountant_CanDraftButCannotWritePayments()
    {
        var permissions = LenseePermissions.ForRole(LenseeRoles.Accountant);

        Assert.Contains(LenseePermissions.PaymentsDraft, permissions);
        Assert.DoesNotContain(LenseePermissions.PaymentsWrite, permissions);
    }
}
