using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Infrastructure.Http;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public class DevBookingServiceClientStubTests
{
    private readonly DevBookingServiceClient _sut = new(NullLogger<DevBookingServiceClient>.Instance);

    [Fact]
    public async Task GetBookingSnapshotAsync_Returns_Success_With_Valid_Data()
    {
        var bookingId = Guid.NewGuid();

        var result = await _sut.GetBookingSnapshotAsync(bookingId);

        result.Kind.Should().Be(BookingLookupOutcomeKind.Success);
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.BookingId.Should().Be(bookingId);
        result.Snapshot.Status.Should().Be("CONFIRMED");
    }
}
