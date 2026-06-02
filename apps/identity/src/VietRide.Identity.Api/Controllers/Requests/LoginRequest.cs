namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/auth/login request body.</summary>
public sealed record LoginRequest(
    string Email,
    string Password);
