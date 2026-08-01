namespace VietRide.Shared.Messaging.Abstractions;

/// <summary>
/// Signals that an integration-event handler failed because a downstream dependency is
/// temporarily unavailable and the broker should apply its configured durable delayed-retry policy.
/// </summary>
public sealed class TransientIntegrationEventException : Exception
{
    public TransientIntegrationEventException(string message)
        : base(message)
    {
    }
}
