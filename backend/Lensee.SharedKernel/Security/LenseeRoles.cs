namespace Lensee.SharedKernel.Security;

public static class LenseeRoles
{
    public const string CLevel = "CLevel";
    public const string Admin = "Admin";
    public const string Accountant = "Accountant";
    public const string WarehouseClerk = "WarehouseClerk";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        CLevel,
        Admin,
        Accountant,
        WarehouseClerk
    };

    public static string Normalize(string? role)
    {
        var normalized = role?.Trim().Replace("-", string.Empty).Replace(" ", string.Empty);

        return normalized switch
        {
            { } value when string.Equals(value, CLevel, StringComparison.OrdinalIgnoreCase) => CLevel,
            { } value when string.Equals(value, Admin, StringComparison.OrdinalIgnoreCase) => Admin,
            { } value when string.Equals(value, Accountant, StringComparison.OrdinalIgnoreCase) => Accountant,
            { } value when string.Equals(value, WarehouseClerk, StringComparison.OrdinalIgnoreCase) => WarehouseClerk,
            _ => string.Empty
        };
    }

    public static string ToDisplayName(string role) =>
        role switch
        {
            CLevel => "C-Level",
            WarehouseClerk => "Warehouse Clerk",
            _ => role
        };
}
