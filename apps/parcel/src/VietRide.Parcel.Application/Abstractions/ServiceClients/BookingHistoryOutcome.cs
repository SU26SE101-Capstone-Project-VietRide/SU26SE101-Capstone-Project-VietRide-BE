namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record BookingHistoryOutcome(
    bool IsSuccess,
    BookingHistoryPage? Page,
    string? ErrorMessage);
