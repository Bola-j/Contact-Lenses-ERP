namespace Lensee.Modules.Inventory.Services;

public readonly record struct InventoryQuantity(int Packs, int Pieces, int PiecesPerPack)
{
    public int TotalPieces => checked(Packs * PiecesPerPack + Pieces);

    public static InventoryQuantity FromTotalPieces(int totalPieces, int piecesPerPack)
    {
        if (totalPieces < 0) throw new ArgumentOutOfRangeException(nameof(totalPieces));
        if (piecesPerPack <= 0) throw new ArgumentOutOfRangeException(nameof(piecesPerPack));
        return new(totalPieces / piecesPerPack, totalPieces % piecesPerPack, piecesPerPack);
    }

    public static InventoryQuantity FromEntry(int packs, int pieces, int piecesPerPack)
    {
        if (packs < 0 || pieces < 0) throw new ArgumentOutOfRangeException(nameof(pieces));
        if (piecesPerPack <= 0) throw new ArgumentOutOfRangeException(nameof(piecesPerPack));
        return FromTotalPieces(checked(packs * piecesPerPack + pieces), piecesPerPack);
    }

    public static int ToTotalPieces(int packs, int pieces, int piecesPerPack) => FromEntry(packs, pieces, piecesPerPack).TotalPieces;
}
