namespace VietRide.Identity.Application.Features.Devices.GetActiveDeviceTokens;

public sealed record GetActiveDeviceTokensResponseDto(string FcmToken, string Platform);
