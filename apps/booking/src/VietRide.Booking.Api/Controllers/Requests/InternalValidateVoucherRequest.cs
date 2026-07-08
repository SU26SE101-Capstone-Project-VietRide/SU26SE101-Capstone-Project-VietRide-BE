namespace VietRide.Booking.Api.Controllers.Requests;

public sealed record InternalValidateVoucherRequest(
    string VoucherCode,
    Guid OperatorId,
    Guid RouteId,
    Guid UserId,
    long OrderAmount,
    string Service,
    string? PaymentMethod);
