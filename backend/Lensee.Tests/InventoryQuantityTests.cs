using Lensee.Modules.Inventory.Services;
using Xunit;

namespace Lensee.Tests;

public sealed class InventoryQuantityTests
{
    [Fact]
    public void Converts_packs_and_remainder_using_sku_pack_size()
    {
        var quantity = InventoryQuantity.FromEntry(12, 3, 6);

        Assert.Equal(75, quantity.TotalPieces);
        Assert.Equal(new InventoryQuantity(12, 3, 6), InventoryQuantity.FromTotalPieces(75, 6));
    }

    [Fact]
    public void Normalizes_loose_piece_count_that_can_become_a_pack()
    {
        Assert.Equal(new InventoryQuantity(2, 0, 6), InventoryQuantity.FromEntry(1, 6, 6));
    }

    [Fact]
    public void Splits_total_pieces_into_full_packs_and_remainder()
    {
        var quantity = InventoryQuantity.FromTotalPieces(17, 6);

        Assert.Equal(2, quantity.Packs);
        Assert.Equal(5, quantity.Pieces);
    }
}
