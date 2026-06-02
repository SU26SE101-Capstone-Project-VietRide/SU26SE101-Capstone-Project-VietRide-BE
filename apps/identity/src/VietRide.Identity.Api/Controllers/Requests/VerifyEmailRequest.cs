namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/auth/verify-email request body.</summary>
public sealed record VerifyEmailRequest(
    string Email,
    string Code,
    string Purpose);
