using Lensee.Modules.Catalog.Data;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Modules.Catalog.Services;

public sealed class CategoryTreeService
{
    public IReadOnlyList<CategoryTreeNode> BuildTree(IReadOnlyList<CategoryTreeItem> categories) =>
        BuildTree(categories, null);

    public async Task<bool> WouldCreateCycleAsync(
        CatalogDbContext dbContext,
        Guid categoryId,
        Guid requestedParentId,
        CancellationToken cancellationToken = default)
    {
        var parentIds = await dbContext.Categories
            .Select(category => new { category.Id, category.ParentId })
            .ToDictionaryAsync(category => category.Id, category => category.ParentId, cancellationToken);

        return WouldCreateCycle(parentIds, categoryId, requestedParentId);
    }

    public bool WouldCreateCycle(
        IReadOnlyDictionary<Guid, Guid?> parentIds,
        Guid categoryId,
        Guid requestedParentId)
    {
        var seen = new HashSet<Guid>();
        var parentId = requestedParentId;
        while (seen.Add(parentId))
        {
            if (parentId == categoryId)
            {
                return true;
            }

            if (!parentIds.TryGetValue(parentId, out var nextParentId) || nextParentId is null)
            {
                return false;
            }

            parentId = nextParentId.Value;
        }

        return true;
    }

    private static IReadOnlyList<CategoryTreeNode> BuildTree(
        IReadOnlyList<CategoryTreeItem> categories,
        Guid? parentId)
    {
        return categories
            .Where(category => category.ParentId == parentId)
            .OrderBy(category => category.Name)
            .Select(category => new CategoryTreeNode(
                category.Id,
                category.ParentId,
                category.Name,
                BuildTree(categories, category.Id)))
            .ToList();
    }
}

public sealed record CategoryTreeItem(Guid Id, Guid? ParentId, string Name);

public sealed record CategoryTreeNode(
    Guid Id,
    Guid? ParentId,
    string Name,
    IReadOnlyList<CategoryTreeNode> Children);
