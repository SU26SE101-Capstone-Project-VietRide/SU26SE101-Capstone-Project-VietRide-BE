namespace VietRide.Identity.Application.Features.Devices.RegisterDeviceToken;

/// <summary>Response DTO for POST /v1/auth/device-token.</summary>
public sealed record RegisterDeviceTokenResponseDto(
    Guid DeviceId,
    bool IsActive);
