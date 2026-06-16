using MediatR;

namespace VietRide.Payment.Application.Features.TopUps.ExpireTopUp;

/// <summary>
/// Expires pending VNPay top-up requests whose 15-minute payment window has elapsed.
/// </summary>
public sealed record ExpireTopUpCommand(DateTimeOffset? Now = null) : IRequest<ExpireTopUpResult>;
