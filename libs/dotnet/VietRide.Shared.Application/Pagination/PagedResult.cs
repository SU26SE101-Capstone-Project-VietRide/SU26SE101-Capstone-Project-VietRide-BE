namespace VietRide.Shared.Application.Pagination;

/// Canonical list response shape per BACKEND_SOURCE_OF_TRUTH 5.4:
/// { items, total, page, pageSize }
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)Total / PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;
}
