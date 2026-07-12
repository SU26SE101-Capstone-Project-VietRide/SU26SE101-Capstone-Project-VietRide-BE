using MediatR;

namespace VietRide.Payment.Application.Features.Internal.Payments.ExpireSubscriptionPayment;

public sealed record ExpireSubscriptionPaymentCommand(Guid PaymentId) : IRequest<bool>;
