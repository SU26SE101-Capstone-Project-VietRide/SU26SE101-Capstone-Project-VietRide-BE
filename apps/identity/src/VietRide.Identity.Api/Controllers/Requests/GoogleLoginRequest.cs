namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/auth/google request body.</summary>
public sealed record GoogleLoginRequest(
    string IdToken);
