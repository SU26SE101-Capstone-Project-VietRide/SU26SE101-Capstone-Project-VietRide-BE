namespace VietRide.Shared.Messaging.RabbitMq;

/// <summary>
/// Bound from configuration section <c>RabbitMq</c>.
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>Configuration section name used by the DI extension.</summary>
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Topic exchange — fixed canonical name for all VietRide events.</summary>
    public string ExchangeName { get; set; } = "vietride.events";

    /// <summary>Declare exchange durable (survives broker restart).</summary>
    public bool ExchangePersistent { get; set; } = true;

    /// <summary>Max number of connection-establishment retries on startup.</summary>
    public int ConnectionRetryCount { get; set; } = 5;

    /// <summary>Base delay (seconds) between connection retries; exponential.</summary>
    public int ConnectionRetryBaseDelaySeconds { get; set; } = 2;
}
