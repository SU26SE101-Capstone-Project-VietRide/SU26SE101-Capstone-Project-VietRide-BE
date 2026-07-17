namespace VietRide.Booking.Application.Abstractions.Repositories;

public sealed record BookingStationMergeApplicationResult(
    bool Applied,
    Guid CanonicalStationId,
    int FlattenedRedirectCount,
    int RelinkedBookingCount)
{
    public static BookingStationMergeApplicationResult Replay(Guid canonicalStationId)
        => new(false, canonicalStationId, 0, 0);
}
