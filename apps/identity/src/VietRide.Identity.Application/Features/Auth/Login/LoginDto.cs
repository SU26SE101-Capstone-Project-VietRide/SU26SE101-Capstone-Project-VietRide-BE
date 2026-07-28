using System.Text.Json.Serialization;

namespace VietRide.Identity.Application.Features.Auth.Login;

/// <summary>Response DTO for POST /v1/auth/login (200) and POST /v1/auth/refresh (200).</summary>
public sealed record TokenBundleDto(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    UserSummaryDto User);

public sealed record UserSummaryDto(
    Guid Id,
    string Email,
    string? Phone,
    string DisplayName,
    string Role,
    Guid? OperatorId,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? AvatarUrl = null);
