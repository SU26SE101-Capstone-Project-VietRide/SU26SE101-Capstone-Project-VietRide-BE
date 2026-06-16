namespace VietRide.Trip.Application.Features.Trips.SearchTrips;

public sealed record SearchTripsResult(
    IReadOnlyList<SearchTripItem> Items,
    int Page,
    int PageSize,
    long TotalItems,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage)
{
    public static SearchTripsResult Create(IReadOnlyList<SearchTripItem> items, int page, int pageSize, long totalItems)
    {
        var totalPages = pageSize <= 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        return new SearchTripsResult(
            items,
            page,
            pageSize,
            totalItems,
            totalPages,
            page < totalPages,
            page > 1 && totalPages > 0);
    }
}
