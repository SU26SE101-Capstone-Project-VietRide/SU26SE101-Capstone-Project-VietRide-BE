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
    private readonly Func<IConnection> _createConnection;

    private IConnection? _connection;
    private bool _disposed;

    public RabbitMqConnectionFactory(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionFactory> logger)
        : this(options, logger, null)
    {
    }

    internal RabbitMqConnectionFactory(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionFactory> logger,
        Func<IConnection>? createConnection)
    {
        _options = options.Value;
        _logger = logger;
        _createConnection = createConnection ?? CreateConnection;
        if (_options.ConnectionAttemptTimeoutSeconds <= 0)
        {
            throw new OptionsValidationException(
                RabbitMqOptions.SectionName,
                typeof(RabbitMqOptions),
                new[] { "RabbitMq:ConnectionAttemptTimeoutSeconds must be greater than zero." });
        }

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
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsOpen(_connection))
                return _connection!;
        }

        var candidate = _retry.Execute(_ => _createConnection(), CancellationToken.None);
        IConnection? connectionToDispose;
        IConnection? installedConnection;
        var disposedWhileCreating = false;

        lock (_lock)
        {
            if (_disposed)
            {
                connectionToDispose = candidate;
                installedConnection = null;
                disposedWhileCreating = true;
            }
            else if (IsOpen(_connection))
            {
                connectionToDispose = candidate;
                installedConnection = _connection;
            }
            else
            {
                connectionToDispose = _connection;
                _connection = candidate;
                installedConnection = candidate;
            }
        }

        DisposeConnection(connectionToDispose);
        if (disposedWhileCreating)
            throw new ObjectDisposedException(nameof(RabbitMqConnectionFactory));

        return installedConnection!;
    }

    private static bool IsOpen(IConnection? connection)
    {
        try
        {
            return connection?.IsOpen == true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void DisposeConnection(IConnection? connection)
    {
        try
        {
            connection?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing unused RabbitMQ connection.");
        }
    }

    private IConnection CreateConnection()
    {
        var factory = CreateClientFactory();

        var conn = factory.CreateConnection("vietride-publisher");
        _logger.LogInformation(
            "RabbitMQ connection established to {Host}:{Port}/{Vhost}.",
            _options.HostName, _options.Port, _options.VirtualHost);
        return conn;
    }

    internal ConnectionFactory CreateClientFactory()
        => new()
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(_options.ConnectionAttemptTimeoutSeconds),
            DispatchConsumersAsync = true,
        };

    public void Dispose()
    {
        IConnection? connection;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            connection = _connection;
            _connection = null;
        }

        try
        {
            if (IsOpen(connection)) connection!.Close();
            connection?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing RabbitMQ connection during dispose.");
        }
    }
}
