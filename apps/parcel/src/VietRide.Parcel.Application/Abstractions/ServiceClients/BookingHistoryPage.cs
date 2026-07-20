namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record BookingHistoryPage(
    IReadOnlyList<BookingHistoryItemDto> Items,
    int Page,
    int PageSize,
    long TotalItems,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage);
