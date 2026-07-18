namespace VietRide.Booking.Application.Abstractions.Repositories;

public interface IBookingStationRedirectRepository
{
    Task<BookingStationMergeApplicationResult> ApplyMergeAsync(
        Guid sourceEventId,
        DateTimeOffset occurredAt,
        Guid primaryStationId,
        Guid duplicateStationId,
        CancellationToken cancellationToken = default);
}
