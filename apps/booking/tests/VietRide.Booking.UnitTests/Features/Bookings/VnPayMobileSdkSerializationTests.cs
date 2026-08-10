using System.Text.Json;
using FluentAssertions;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Bookings.CreateBooking;
using VietRide.Booking.Application.Features.Bookings.CreateRoundTripBooking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class VnPayMobileSdkSerializationTests
{
    [Fact]
    public void PublicBookingResults_SerializeExactVnpaySdkContract()
    {
        var sdk = new VnPaySdkMetadata("TESTTMN", "vietride", true);
        var leg = new CreateRoundTripBookingResult.RoundTripBookingResult(
            Guid.NewGuid(),
            "VR-TEST",
            150_000,
            0,
            []);
        object[] results =
        [
            new CreateBookingResult(
                Guid.NewGuid(),
                "VR-TEST",
                "PENDING_PAYMENT",
                150_000,
                0,
                Guid.NewGuid(),
                "https://pay.test",
                [],
                "MOBILE_SDK",
                sdk),
            new CreateRoundTripBookingResult(
                Guid.NewGuid(),
                leg,
                leg,
                300_000,
                Guid.NewGuid(),
                "PENDING_PAYMENT",
                "https://pay.test",
                "MOBILE_SDK",
                sdk),
        ];

        foreach (var result in results)
            AssertVnPaySdkJson(result);
    }

    private static void AssertVnPaySdkJson(object result)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            result,
            result.GetType(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        json.RootElement.TryGetProperty("vnpaySdk", out var sdk).Should().BeTrue();
        json.RootElement.TryGetProperty("vnPaySdk", out _).Should().BeFalse();
        sdk.GetProperty("tmnCode").GetString().Should().Be("TESTTMN");
        sdk.GetProperty("scheme").GetString().Should().Be("vietride");
        sdk.GetProperty("isSandbox").GetBoolean().Should().BeTrue();
    }
}
