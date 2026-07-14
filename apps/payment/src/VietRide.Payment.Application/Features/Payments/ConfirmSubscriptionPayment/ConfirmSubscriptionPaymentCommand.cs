using MediatR;
using VietRide.Payment.Application.Features.Payments.ConfirmBookingPayment;

namespace VietRide.Payment.Application.Features.Payments.ConfirmSubscriptionPayment;

public sealed record ConfirmSubscriptionPaymentCommand(
    IReadOnlyDictionary<string, string> Parameters) : IRequest<ConfirmBookingPaymentResult>;
