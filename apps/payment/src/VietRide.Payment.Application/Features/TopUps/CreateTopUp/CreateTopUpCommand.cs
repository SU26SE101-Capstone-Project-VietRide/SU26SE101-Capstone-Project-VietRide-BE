using MediatR;

namespace VietRide.Payment.Application.Features.TopUps.CreateTopUp;

/// <summary>
/// Command for POST /v1/wallet/top-up. Authenticated passenger id is resolved by the controller.
/// Idempotency-Key is handled by the shared middleware before this command reaches MediatR.
/// </summary>
public sealed record CreateTopUpCommand(
    Guid UserId,
    long Amount,
    string Method,
    string ClientIpAddress) : IRequest<CreateTopUpResult>;
