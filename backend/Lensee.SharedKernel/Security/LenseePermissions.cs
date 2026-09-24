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
    public const string SupplyPaymentsApprove = "supply.payments.approve";
    public const string AuditRead = "audit.read";
    public const string SettingsWrite = "settings.write";
    public const string IntegrationsShopifyRead = "integrations.shopify.read";
    public const string IntegrationsShopifyManage = "integrations.shopify.manage";
    public const string FinanceRead = "finance.read";
    public const string FinanceExpenseCreate = "finance.expense.create";
    public const string FinanceExpenseApprove = "finance.expense.approve";
    public const string FinanceWithdrawalCreate = "finance.withdrawal.create";
    public const string FinanceWithdrawalAssign = "finance.withdrawal.assign";
    public const string FinanceWithdrawalApprove = "finance.withdrawal.approve";
    public const string FinanceAccountsManage = "finance.accounts.manage";
    public const string FinanceReconcile = "finance.reconcile";
    public const string FinanceOpeningCreate = "finance.opening.create";
    public const string FinanceOpeningCorrect = "finance.opening.correct";
    public const string ExecutiveSummaryRead = "reports.executive.read";

    public static IReadOnlyCollection<string> ForRole(string role) =>
        LenseeRoles.Normalize(role) switch
        {
            LenseeRoles.CLevel => new[]
            {
                CatalogRead, InventoryRead, OperationsRead, PaymentsRead, PaymentsAdjustmentsApprove, ReportsRead, ExecutiveSummaryRead, SupplyRead, IntegrationsShopifyRead, FinanceRead
            },
            LenseeRoles.Admin => new[]
            {
                UsersRead, UsersWrite, UsersPasswordWrite, UsersDelete, CatalogRead, CatalogWrite, InventoryRead, InventoryWrite,
                OperationsRead, OperationsWrite, OperationsCorrectionsRequest, OperationsCorrectionsApprove,
                PaymentsRead, PaymentsWrite, PaymentsDraft, PaymentsApprove, PaymentsAdjustmentsRequest, PaymentsAdjustmentsApprove,
                ReportsRead, ExecutiveSummaryRead, SupplyRead, SupplyWrite, SupplyPaymentsApprove, AuditRead, SettingsWrite, IntegrationsShopifyRead, IntegrationsShopifyManage, FinanceRead, FinanceExpenseCreate, FinanceExpenseApprove, FinanceWithdrawalCreate, FinanceWithdrawalAssign, FinanceWithdrawalApprove, FinanceAccountsManage, FinanceReconcile, FinanceOpeningCreate, FinanceOpeningCorrect
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
                OperationsRead, OperationsCorrectionsRequest, PaymentsRead, PaymentsDraft, PaymentsAdjustmentsRequest, ReportsRead, FinanceRead, FinanceOpeningCreate, FinanceOpeningCorrect
            },
            LenseeRoles.WarehouseClerk => new[]
            {
                CatalogRead, InventoryRead, OperationsRead, OperationsWrite, IntegrationsShopifyRead, IntegrationsShopifyManage
            },
            _ => Array.Empty<string>()
        };
}
