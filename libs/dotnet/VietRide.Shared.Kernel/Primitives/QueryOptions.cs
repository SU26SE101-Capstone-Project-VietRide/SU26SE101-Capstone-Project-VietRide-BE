namespace VietRide.Shared.Kernel.Primitives;

/// <summary>
/// Standard query-string parameters for list/collection endpoints (ADR 0004 §Decision Rule 6).
/// Bound from <c>?page=1&amp;pageSize=20&amp;search=...&amp;sortBy=createdAt&amp;sortDir=desc&amp;includeDeleted=false</c>.
/// </summary>
public sealed class QueryOptions
{
    private const string SortAscending = "asc";
    private const string SortDescending = "desc";

    private int _page = 1;
    private int _pageSize = 20;
    private string _sortDir = SortDescending;

    /// <summary>1-based page number. Defaults to 1.</summary>
    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    /// <summary>Items per page. Clamped to 1–100 (BSOT §5.7). Defaults to 20.</summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value < 1 ? 1 : value > 100 ? 100 : value;
    }

    /// <summary>Free-text search term. Null/empty = no search.</summary>
    public string? Search { get; init; }

    /// <summary>
    /// Comma-separated list of fields to search in (e.g. <c>email,phone</c>).
    /// Whitelisted per aggregate at the repository layer (security requirement).
    /// </summary>
    public string? SearchIn { get; init; }

    /// <summary>Field to sort by. Whitelisted per aggregate at the repository layer.</summary>
    public string? SortBy { get; init; }

    /// <summary>Sort direction — <c>asc</c> or <c>desc</c>. Defaults to <c>desc</c>.</summary>
    public string SortDir
    {
        get => _sortDir;
        init => _sortDir = NormalizeSortDir(value);
    }

    /// <summary>
    /// When <c>true</c>, include soft-deleted records (ADR 0003).
    /// Only admin/privileged endpoints should expose this. Defaults to <c>false</c>.
    /// </summary>
    public bool IncludeDeleted { get; init; } = false;

    private static string NormalizeSortDir(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();

        return normalized switch
        {
            SortAscending => SortAscending,
            SortDescending => SortDescending,
            _ => throw new ArgumentException("SortDir must be 'asc' or 'desc'.", nameof(value)),
        };
    }
}
