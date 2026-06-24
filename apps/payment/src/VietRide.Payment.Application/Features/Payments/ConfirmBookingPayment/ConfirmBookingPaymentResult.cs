using System.Text.Json.Serialization;

namespace VietRide.Payment.Application.Features.Payments.ConfirmBookingPayment;

public sealed record ConfirmBookingPaymentResult(
    [property: JsonPropertyName("RspCode")] string RspCode,
    [property: JsonPropertyName("Message")] string Message,
    [property: JsonIgnore] int StatusCode);
