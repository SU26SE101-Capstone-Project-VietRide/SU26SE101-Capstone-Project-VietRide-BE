namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/auth/resend-verification-email request body.</summary>
public sealed record ResendVerificationEmailRequest(
    string Email,
    string Purpose);
