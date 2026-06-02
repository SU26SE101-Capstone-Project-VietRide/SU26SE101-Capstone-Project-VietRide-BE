namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/auth/register request body.</summary>
public sealed record RegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    string Phone);
