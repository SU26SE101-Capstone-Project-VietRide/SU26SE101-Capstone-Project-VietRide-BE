using System.Text.Json.Serialization;

namespace VietRide.Payment.Application.Features.Payments.DispatchVnPayIpn;

public sealed record DispatchVnPayIpnResult(
    [property: JsonPropertyName("RspCode")] string RspCode,
    [property: JsonPropertyName("Message")] string Message);
