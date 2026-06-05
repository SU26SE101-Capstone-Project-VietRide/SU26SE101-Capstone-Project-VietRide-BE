namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/auth/device-token request body.</summary>
public sealed record RegisterDeviceTokenRequest(
    string FcmToken,
    string Platform);
