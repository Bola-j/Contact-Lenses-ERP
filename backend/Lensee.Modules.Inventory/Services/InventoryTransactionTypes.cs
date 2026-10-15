namespace Lensee.Modules.Inventory.Services;

public static class InventoryTransactionTypes
{
    public const string Receipt = "Receipt";
    public const string Sale = "Sale";
    public const string ReserveInWarehouse = "ReserveInWarehouse";
    public const string ReserveWithRep = "ReserveWithRep";
    public const string ReserveReleaseInWarehouse = "ReserveReleaseInWarehouse";
    public const string ReserveReleaseWithRep = "ReserveReleaseWithRep";
    public const string WriteOff = "WriteOff";
    public const string SupplyOut = "SupplyOut";
    public const string SupplyIn = "SupplyIn";
    public const string StocktakeAdjustment = "StocktakeAdjustment";
    public const string ChangeOut = "ChangeOut";
    public const string ChangeIn = "ChangeIn";
    public const string ReturnIn = "ReturnIn";
    public const string ReturnRestore = "ReturnRestore";
    public const string PackOpen = "PackOpen";
    public const string QuarantineIn = "QuarantineIn";
    public const string QuarantineRelease = "QuarantineRelease";
    public const string QuarantineWriteOff = "QuarantineWriteOff";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        Receipt,
        Sale,
        ReserveInWarehouse,
        ReserveWithRep,
        ReserveReleaseInWarehouse,
        ReserveReleaseWithRep,
        WriteOff,
        SupplyOut,
        SupplyIn,
        StocktakeAdjustment,
        ChangeOut,
        ChangeIn,
        ReturnIn,
        ReturnRestore,
        PackOpen,
        QuarantineIn,
        QuarantineRelease,
        QuarantineWriteOff
    };

    public static bool IsValid(string value) => Allowed.Contains(value);
}
