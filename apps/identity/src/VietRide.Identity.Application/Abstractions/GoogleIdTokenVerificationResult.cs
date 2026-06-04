namespace VietRide.Identity.Application.Abstractions;

public sealed record GoogleIdTokenVerificationResult(
    string Subject,
    string Email,
    string? DisplayName,
    string? AvatarUrl);
