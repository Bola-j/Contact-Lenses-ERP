namespace Lensee.SharedKernel.Security;

public static class LenseePermissions
{
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string UsersPasswordWrite = "users.password.write";
    public const string CatalogRead = "catalog.read";
    public const string CatalogWrite = "catalog.write";
    public const string InventoryRead = "inventory.read";
    public const string InventoryWrite = "inventory.write";
    public const string OperationsRead = "operations.read";
    public const string OperationsWrite = "operations.write";
    public const string PaymentsRead = "payments.read";
    public const string PaymentsWrite = "payments.write";
    public const string PaymentsDraft = "payments.draft";
    public const string PaymentsApprove = "payments.approve";
    public const string ReportsRead = "reports.read";
    public const string SupplyRead = "supply.read";
    public const string SupplyWrite = "supply.write";
    public const string AuditRead = "audit.read";
    public const string SettingsWrite = "settings.write";

    public static IReadOnlyCollection<string> ForRole(string role) =>
        LenseeRoles.Normalize(role) switch
        {
            LenseeRoles.CLevel => new[]
            {
                CatalogRead, InventoryRead, OperationsRead, PaymentsRead, ReportsRead, SupplyRead
            },
            LenseeRoles.Admin => new[]
            {
                UsersRead, UsersWrite, UsersPasswordWrite, CatalogRead, CatalogWrite, InventoryRead, InventoryWrite,
                OperationsRead, OperationsWrite, PaymentsRead, PaymentsWrite, PaymentsDraft, PaymentsApprove,
                ReportsRead, SupplyRead, SupplyWrite, AuditRead, SettingsWrite
            },
            LenseeRoles.ERPAdmin => new[]
            {
                UsersRead, UsersWrite, CatalogRead, CatalogWrite, InventoryRead, InventoryWrite,
                OperationsRead, OperationsWrite, PaymentsRead, PaymentsWrite, PaymentsDraft, PaymentsApprove,
                ReportsRead, AuditRead, SettingsWrite
            },
            LenseeRoles.Accountant => new[]
            {
                OperationsRead, PaymentsRead, PaymentsDraft, PaymentsApprove, ReportsRead
            },
            LenseeRoles.WarehouseClerk => new[]
            {
                CatalogRead, InventoryRead, OperationsRead, OperationsWrite
            },
            _ => Array.Empty<string>()
        };
}
