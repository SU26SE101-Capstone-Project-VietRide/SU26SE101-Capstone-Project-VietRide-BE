namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/auth/logout request body.</summary>
public sealed record LogoutRequest(string RefreshToken);
