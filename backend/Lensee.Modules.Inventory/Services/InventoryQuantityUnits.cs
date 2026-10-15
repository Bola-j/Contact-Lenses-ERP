namespace Lensee.Modules.Inventory.Services;

public static class InventoryQuantityUnits
{
    public const string Pack = "Pack";
    public const string Piece = "Piece";

    public static bool IsValid(string value) =>
        string.Equals(value, Pack, StringComparison.Ordinal) ||
        string.Equals(value, Piece, StringComparison.Ordinal);
}
