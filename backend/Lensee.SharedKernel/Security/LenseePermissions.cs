namespace Lensee.SharedKernel.Security;

public static class LenseePermissions
{
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string UsersPasswordWrite = "users.password.write";
    public const string UsersDelete = "users.delete";
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
    public const string PaymentsAdjustmentsRequest = "payments.adjustments.request";
    public const string PaymentsAdjustmentsApprove = "payments.adjustments.approve";
    public const string OperationsCorrectionsRequest = "operations.corrections.request";
    public const string OperationsCorrectionsApprove = "operations.corrections.approve";
    public const string ReportsRead = "reports.read";
    public const string SupplyRead = "supply.read";
    public const string SupplyWrite = "supply.write";
    public const string AuditRead = "audit.read";
    public const string SettingsWrite = "settings.write";
    public const string IntegrationsShopifyRead = "integrations.shopify.read";
    public const string IntegrationsShopifyManage = "integrations.shopify.manage";

    public static IReadOnlyCollection<string> ForRole(string role) =>
        LenseeRoles.Normalize(role) switch
        {
            LenseeRoles.CLevel => new[]
            {
                CatalogRead, InventoryRead, OperationsRead, PaymentsRead, ReportsRead, SupplyRead, IntegrationsShopifyRead
            },
            LenseeRoles.Admin => new[]
            {
                UsersRead, UsersWrite, UsersPasswordWrite, UsersDelete, CatalogRead, CatalogWrite, InventoryRead, InventoryWrite,
                OperationsRead, OperationsWrite, OperationsCorrectionsRequest, OperationsCorrectionsApprove,
                PaymentsRead, PaymentsWrite, PaymentsDraft, PaymentsApprove, PaymentsAdjustmentsRequest, PaymentsAdjustmentsApprove,
                ReportsRead, SupplyRead, SupplyWrite, AuditRead, SettingsWrite, IntegrationsShopifyRead, IntegrationsShopifyManage
            },
            LenseeRoles.ERPAdmin => new[]
            {
                UsersRead, UsersWrite, CatalogRead, CatalogWrite, InventoryRead, InventoryWrite,
                OperationsRead, OperationsWrite, OperationsCorrectionsRequest, OperationsCorrectionsApprove,
                PaymentsRead, PaymentsWrite, PaymentsDraft, PaymentsApprove, PaymentsAdjustmentsRequest, PaymentsAdjustmentsApprove,
                ReportsRead, AuditRead, SettingsWrite, IntegrationsShopifyRead, IntegrationsShopifyManage
            },
            LenseeRoles.Accountant => new[]
            {
                OperationsRead, OperationsCorrectionsRequest, PaymentsRead, PaymentsDraft, PaymentsAdjustmentsRequest, ReportsRead
            },
            LenseeRoles.WarehouseClerk => new[]
            {
                CatalogRead, InventoryRead, OperationsRead, OperationsWrite, IntegrationsShopifyRead, IntegrationsShopifyManage
            },
            _ => Array.Empty<string>()
        };
}
