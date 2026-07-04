using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class DevBookingServiceClient : IBookingServiceClient
{
    private readonly ILogger<DevBookingServiceClient> _logger;

    public DevBookingServiceClient(ILogger<DevBookingServiceClient> logger)
    {
        _logger = logger;
    }

    public Task<BookingLookupOutcome> GetBookingSnapshotAsync(
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Booking stub for GetBookingSnapshotAsync({BookingId}).", bookingId);

        var snapshot = new BookingSnapshot(
            BookingId: bookingId,
            UserId: Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            TripId: Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
            Status: "CONFIRMED");

        return Task.FromResult(new BookingLookupOutcome(BookingLookupOutcomeKind.Success, snapshot, null));
    }
}
