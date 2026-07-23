namespace VietRide.Shared.Application.Inbox;

/// <summary>
/// Executes one broker delivery inside the consumer service's durable inbox transaction.
/// </summary>
public interface IIntegrationEventInbox
{
    Task<IntegrationEventInboxResult> ExecuteAsync(
        string consumerName,
        Guid messageId,
        string payloadHash,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}

public enum IntegrationEventInboxResult
{
    Processed,
    Duplicate,
}

public sealed class IntegrationEventPayloadMismatchException : Exception
{
    public IntegrationEventPayloadMismatchException(string consumerName, Guid messageId)
        : base($"RabbitMQ MessageId '{messageId:D}' was reused with a different payload for '{consumerName}'.")
    {
    }
}
