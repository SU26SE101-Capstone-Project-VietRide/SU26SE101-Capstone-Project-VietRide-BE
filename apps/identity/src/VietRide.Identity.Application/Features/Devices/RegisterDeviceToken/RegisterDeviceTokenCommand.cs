using MediatR;

namespace VietRide.Identity.Application.Features.Devices.RegisterDeviceToken;

/// <summary>Command for POST /v1/auth/device-token.</summary>
public sealed record RegisterDeviceTokenCommand(
    Guid UserId,
    string FcmToken,
    string Platform) : IRequest<RegisterDeviceTokenResponseDto>;
