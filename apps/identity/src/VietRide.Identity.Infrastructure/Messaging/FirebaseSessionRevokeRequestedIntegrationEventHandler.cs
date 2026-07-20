using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class FirebaseSessionRevokeRequestedIntegrationEventHandler
    : IIntegrationEventHandler<FirebaseSessionRevokeRequestedIntegrationEvent>
{
    private readonly IFirebaseAuthService _firebaseAuth;
    private readonly ILogger<FirebaseSessionRevokeRequestedIntegrationEventHandler> _logger;

    public FirebaseSessionRevokeRequestedIntegrationEventHandler(
        IFirebaseAuthService firebaseAuth,
        ILogger<FirebaseSessionRevokeRequestedIntegrationEventHandler> logger)
    {
        _firebaseAuth = firebaseAuth;
        _logger = logger;
    }

    public async Task HandleAsync(
        FirebaseSessionRevokeRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _firebaseAuth.RevokeRefreshTokensAsync(integrationEvent.UserId, cancellationToken);
        _logger.LogInformation(
            "Firebase sessions revoked for user {UserId}; reason {Reason}; event {EventId}.",
            integrationEvent.UserId,
            integrationEvent.Reason,
            integrationEvent.EventId);
    }
}
