namespace VietRide.Shared.Persistence.Inbox;

public sealed class IntegrationInboxRecord
{
    private IntegrationInboxRecord()
    {
    }

    public Guid Id { get; private set; }
    public string ConsumerName { get; private set; } = string.Empty;
    public Guid MessageId { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; private set; }

    public static IntegrationInboxRecord Create(
        string consumerName,
        Guid messageId,
        string payloadHash,
        DateTimeOffset processedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);
        if (messageId == Guid.Empty)
            throw new ArgumentException("Message id is required.", nameof(messageId));

        return new IntegrationInboxRecord
        {
            Id = Guid.NewGuid(),
            ConsumerName = consumerName,
            MessageId = messageId,
            PayloadHash = payloadHash,
            ProcessedAt = processedAt,
        };
    }
}
