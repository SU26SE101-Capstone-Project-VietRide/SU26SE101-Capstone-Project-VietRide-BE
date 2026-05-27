using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace VietRide.Shared.Messaging.RabbitMq;

/// <summary>
/// Owns the single <see cref="IConnection"/> for the process. Created lazily
/// + recreated on demand if dropped. Channels (<see cref="IModel"/>) are
/// cheap — callers create per publish.
/// </summary>
public interface IRabbitMqConnectionFactory : IDisposable
{
    IConnection GetOrCreate();
}

/// <inheritdoc cref="IRabbitMqConnectionFactory"/>
public sealed class RabbitMqConnectionFactory : IRabbitMqConnectionFactory
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionFactory> _logger;
    private readonly object _lock = new();
    private readonly ResiliencePipeline _retry;

    private IConnection? _connection;
    private bool _disposed;

    public RabbitMqConnectionFactory(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionFactory> logger)
    {
        _options = options.Value;
        _logger = logger;

        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<BrokerUnreachableException>()
                    .Handle<RabbitMQClientException>()
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = _options.ConnectionRetryCount,
                Delay = TimeSpan.FromSeconds(_options.ConnectionRetryBaseDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "RabbitMQ connection attempt {Attempt} failed; retrying in {Delay}s.",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalSeconds);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    public IConnection GetOrCreate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connection is { IsOpen: true })
            return _connection;

        lock (_lock)
        {
            if (_connection is { IsOpen: true })
                return _connection;

            _connection?.Dispose();
            _connection = _retry.Execute(_ => CreateConnection(), CancellationToken.None);
            return _connection;
        }
    }

    private IConnection CreateConnection()
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            DispatchConsumersAsync = true,
        };

        var conn = factory.CreateConnection("vietride-publisher");
        _logger.LogInformation(
            "RabbitMQ connection established to {Host}:{Port}/{Vhost}.",
            _options.HostName, _options.Port, _options.VirtualHost);
        return conn;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_connection is { IsOpen: true }) _connection.Close();
            _connection?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing RabbitMQ connection during dispose.");
        }
    }
}
