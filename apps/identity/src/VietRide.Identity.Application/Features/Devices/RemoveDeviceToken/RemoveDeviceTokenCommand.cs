using MediatR;

namespace VietRide.Identity.Application.Features.Devices.RemoveDeviceToken;

/// <summary>Command for DELETE /v1/auth/device-token.</summary>
public sealed record RemoveDeviceTokenCommand(
    Guid UserId,
    string? FcmToken) : IRequest<Unit>;
