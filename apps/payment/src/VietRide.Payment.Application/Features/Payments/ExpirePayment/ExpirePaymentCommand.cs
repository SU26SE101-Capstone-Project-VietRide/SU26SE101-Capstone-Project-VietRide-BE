using MediatR;

namespace VietRide.Payment.Application.Features.Payments.ExpirePayment;

/// <summary>
/// Expires stale VNPay booking payments whose 10-minute payment window has elapsed.
/// </summary>
public sealed record ExpirePaymentCommand(DateTimeOffset? Now = null) : IRequest<ExpirePaymentResult>;
