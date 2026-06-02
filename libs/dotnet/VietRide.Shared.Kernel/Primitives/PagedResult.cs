namespace VietRide.Shared.Kernel.Primitives;

/// <summary>
/// Paginated list result returned by list/query handlers (ADR 0004 §Decision Rule 1 / Rule 6).
/// Consumers place this as the <c>Data</c> payload inside <see cref="ApiResponse{T}"/>.
/// </summary>
public sealed record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalItems { get; init; }
    public int TotalPages { get; init; }
    public bool HasNextPage { get; init; }
    public bool HasPreviousPage { get; init; }

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, long totalItems)
    {
        var totalPages = pageSize > 0 ? (int)Math.Ceiling((double)totalItems / pageSize) : 0;
        return new PagedResult<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            HasNextPage = page < totalPages,
            HasPreviousPage = page > 1,
        };
    }
}
