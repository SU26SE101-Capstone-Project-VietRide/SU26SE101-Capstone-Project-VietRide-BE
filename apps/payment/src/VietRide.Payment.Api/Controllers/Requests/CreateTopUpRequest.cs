namespace VietRide.Payment.Api.Controllers.Requests;

public sealed record CreateTopUpRequest(
    long Amount,
    string Method,
    string? PaymentReturnMode = null);
