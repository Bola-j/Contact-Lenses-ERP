namespace Lensee.SharedKernel.Primitives;

public sealed record PageRequest(int Page = 1, int PageSize = 25)
{
    public int Page { get; init; } = Math.Max(1, Page);

    public int PageSize { get; init; } = Math.Clamp(PageSize, 1, 100);

    public int Skip => (Page - 1) * PageSize;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}
