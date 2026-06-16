using System.Text.Json.Serialization;

namespace VietRide.Payment.Application.Features.TopUps.ConfirmTopUp;

public sealed record ConfirmTopUpResult(
    [property: JsonPropertyName("RspCode")] string RspCode,
    [property: JsonPropertyName("Message")] string Message,
    [property: JsonIgnore] int StatusCode);
