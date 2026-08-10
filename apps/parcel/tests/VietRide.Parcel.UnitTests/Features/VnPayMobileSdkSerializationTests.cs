using System.Text.Json;
using FluentAssertions;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.DepositPayment;
using VietRide.Parcel.Application.Features.Parcels.FinalPayment;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class VnPayMobileSdkSerializationTests
{
    [Fact]
    public void PublicParcelResults_SerializeExactVnpaySdkContract()
    {
        var sdk = new VnPaySdkMetadata("TESTTMN", "vietride", true);
        object[] results =
        [
            new ParcelDepositPaymentResponse(
                Guid.NewGuid(),
                "PENDING_PAYMENT",
                Guid.NewGuid(),
                150_000,
                0,
                DateTimeOffset.UtcNow.AddMinutes(10),
                "https://pay.test",
                "MOBILE_SDK",
                sdk),
            new ParcelFinalPaymentResponse(
                Guid.NewGuid(),
                "PENDING_FINAL_PAYMENT",
                Guid.NewGuid(),
                150_000,
                0,
                DateTimeOffset.UtcNow.AddMinutes(10),
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
