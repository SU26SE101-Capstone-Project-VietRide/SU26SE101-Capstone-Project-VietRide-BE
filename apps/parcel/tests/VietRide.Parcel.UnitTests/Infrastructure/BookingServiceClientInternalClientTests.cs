using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Infrastructure.Http;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public class BookingServiceClientInternalClientTests
{
    private static readonly Guid BookingId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private FakeMessageHandler _handler = null!;

    [Fact]
    public async Task GetBookingSnapshotAsync_Sends_Request_To_Correct_Path()
    {
        var body = JsonSerializer.Serialize(new
        {
            bookingId = BookingId,
            userId = Guid.NewGuid(),
            tripId = Guid.NewGuid(),
            status = "CONFIRMED",
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, body);

        await client.GetBookingSnapshotAsync(BookingId);

        _handler.LastRequest.Should().NotBeNull();
        _handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be($"/internal/v1/bookings/{BookingId:D}");
        _handler.LastRequest.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task GetBookingSnapshotAsync_Returns_Success_On_200()
    {
        var body = JsonSerializer.Serialize(new
        {
            bookingId = BookingId,
            userId = Guid.NewGuid(),
            tripId = Guid.NewGuid(),
            status = "CONFIRMED",
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, body);

        var result = await client.GetBookingSnapshotAsync(BookingId);

        result.Kind.Should().Be(BookingLookupOutcomeKind.Success);
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.BookingId.Should().Be(BookingId);
        result.Snapshot.Status.Should().Be("CONFIRMED");
    }

    [Fact]
    public async Task GetBookingSnapshotAsync_Returns_BookingNotFound_On_404()
    {
        var client = BuildClient(HttpStatusCode.NotFound, "{}");

        var result = await client.GetBookingSnapshotAsync(BookingId);

        result.Kind.Should().Be(BookingLookupOutcomeKind.BookingNotFound);
        result.Snapshot.Should().BeNull();
    }

    [Fact]
    public async Task GetBookingSnapshotAsync_Returns_TransportError_On_5xx()
    {
        var client = BuildClient(HttpStatusCode.InternalServerError, "{}");

        var result = await client.GetBookingSnapshotAsync(BookingId);

        result.Kind.Should().Be(BookingLookupOutcomeKind.TransportError);
    }

    private BookingServiceClient BuildClient(HttpStatusCode status, string body)
    {
        _handler = new FakeMessageHandler(status, body);
        var httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("http://booking-service"),
        };
        return new BookingServiceClient(httpClient, NullLogger<BookingServiceClient>.Instance);
    }
}
