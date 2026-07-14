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
