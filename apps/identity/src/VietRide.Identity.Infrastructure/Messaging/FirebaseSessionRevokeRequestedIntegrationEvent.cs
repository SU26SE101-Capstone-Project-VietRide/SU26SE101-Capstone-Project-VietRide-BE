using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class FirebaseSessionRevokeRequestedIntegrationEvent : IntegrationEventBase
{
    public override string EventType => "identity.firebase_session.revoke_requested";
    public Guid UserId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
