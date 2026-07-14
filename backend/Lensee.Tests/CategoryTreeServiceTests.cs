using Lensee.Modules.Catalog.Services;
using Xunit;

namespace Lensee.Tests;

public sealed class CategoryTreeServiceTests
{
    [Fact]
    public void BuildTree_NestsCategoriesAndOrdersSiblingsByName()
    {
        var root = Guid.NewGuid();
        var childA = Guid.NewGuid();
        var childB = Guid.NewGuid();
        var service = new CategoryTreeService();

        var tree = service.BuildTree([
            new CategoryTreeItem(childB, root, "Medical Lenses"),
            new CategoryTreeItem(root, null, "Products"),
            new CategoryTreeItem(childA, root, "Colored Lenses")
        ]);

        Assert.Single(tree);
        Assert.Equal(root, tree[0].Id);
        Assert.Collection(
            tree[0].Children,
            child => Assert.Equal(childA, child.Id),
            child => Assert.Equal(childB, child.Id));
    }

    [Fact]
    public void WouldCreateCycle_ReturnsTrue_WhenMovingCategoryUnderDescendant()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var grandchild = Guid.NewGuid();
        var service = new CategoryTreeService();
        var parents = new Dictionary<Guid, Guid?>
        {
            [root] = null,
            [child] = root,
            [grandchild] = child
        };

        var createsCycle = service.WouldCreateCycle(parents, root, grandchild);

        Assert.True(createsCycle);
    }

    [Fact]
    public void WouldCreateCycle_ReturnsFalse_WhenMovingCategoryUnderIndependentParent()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var otherRoot = Guid.NewGuid();
        var service = new CategoryTreeService();
        var parents = new Dictionary<Guid, Guid?>
        {
            [root] = null,
            [child] = root,
            [otherRoot] = null
        };

        var createsCycle = service.WouldCreateCycle(parents, child, otherRoot);

        Assert.False(createsCycle);
    }
}

