namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface IBookingServiceClient
{
    Task<BookingLookupOutcome> GetBookingSnapshotAsync(
        Guid bookingId,
        CancellationToken cancellationToken = default);
}
