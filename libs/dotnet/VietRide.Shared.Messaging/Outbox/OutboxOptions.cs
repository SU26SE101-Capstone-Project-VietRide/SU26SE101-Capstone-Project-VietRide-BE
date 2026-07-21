namespace VietRide.Shared.Messaging.Outbox;

/// <summary>
/// Tuning knobs for <see cref="OutboxBackgroundService"/>. Bound from the
/// optional <c>RabbitMq:Outbox</c> sub-section; defaults match
/// BACKEND_SOURCE_OF_TRUTH guidance (poll 5s, batch 50, terminal after failure six).
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "RabbitMq:Outbox";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 50;
    public int MaxRetryCount { get; set; } = 5;
    public TimeSpan BackoffBaseDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan BackoffMaxDelay { get; set; } = TimeSpan.FromMinutes(15);
}
