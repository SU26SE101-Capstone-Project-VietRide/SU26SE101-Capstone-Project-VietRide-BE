using System.Text.Json.Serialization;

namespace VietRide.Identity.Application.Events;

/// <summary>
/// Integration event emitted when an OTP email is requested (BSOT §7.3).
/// Serializes to camelCase keys: { userId, email, code, purpose, ttlMinutes }.
/// Consumed by Notification Service to deliver the OTP email asynchronously.
/// </summary>
public sealed record OtpRequestedIntegrationEvent(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("ttlMinutes")] int TtlMinutes)
{
    public const string EventType = "identity.otp.requested";
}
