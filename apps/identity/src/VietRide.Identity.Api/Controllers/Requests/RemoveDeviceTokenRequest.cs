namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>DELETE /v1/auth/device-token request body.</summary>
public sealed record RemoveDeviceTokenRequest(string? FcmToken);
