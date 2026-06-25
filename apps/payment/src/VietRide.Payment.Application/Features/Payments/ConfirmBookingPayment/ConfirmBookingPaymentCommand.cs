using MediatR;

namespace VietRide.Payment.Application.Features.Payments.ConfirmBookingPayment;

public sealed record ConfirmBookingPaymentCommand(
    IReadOnlyDictionary<string, string> Parameters) : IRequest<ConfirmBookingPaymentResult>;
