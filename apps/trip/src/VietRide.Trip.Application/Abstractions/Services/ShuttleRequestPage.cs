namespace VietRide.Trip.Application.Abstractions.Services;

public sealed record ShuttleRequestPage(
    IReadOnlyList<ShuttleRequestTripGroup> Items,
    int Page,
    int PageSize,
    long TotalItems,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage,
    ShuttleRequestSummary Summary)
{
    public static ShuttleRequestPage Create(
        IReadOnlyList<ShuttleRequestTripGroup> items,
        int page,
        int pageSize,
        long totalItems,
        long totalPendingPassengerCount)
    {
        var totalPages = pageSize > 0 ? (int)Math.Ceiling((double)totalItems / pageSize) : 0;
        return new ShuttleRequestPage(
            items,
            page,
            pageSize,
            totalItems,
            totalPages,
            page < totalPages,
            page > 1,
            new ShuttleRequestSummary(totalPendingPassengerCount, totalItems));
    }
}

public sealed record ShuttleRequestSummary(
    long TotalPendingPassengerCount,
    long TotalPendingGroupCount);
