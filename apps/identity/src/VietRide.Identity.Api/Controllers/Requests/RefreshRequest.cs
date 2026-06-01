namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/auth/refresh request body.</summary>
public sealed record RefreshRequest(string RefreshToken);
