using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Shared.Messaging.Outbox;

/// <summary>
/// Polls the per-service outbox table on a fixed interval, publishes each
/// pending row to RabbitMQ via <see cref="IEventPublisher"/>, and marks
/// the row processed. Retry is bounded by poll cadence: a failed row is
/// re-fetched on the next tick and retried until its RetryCount exceeds
/// <see cref="OutboxOptions.MaxRetryCount"/>, after which it is parked. There
/// is no per-row delay — <c>ComputeBackoff</c> is computed and logged for
/// visibility but is NOT persisted and does NOT gate fetching.
/// </summary>
/// <remarks>
/// Per BACKEND_SOURCE_OF_TRUTH section 4.3 + 11.x: each service owns its
/// own outbox table written inside the business write transaction
/// (<c>OutboxInterceptor</c>). This worker provides at-least-once
/// delivery — consumers MUST be idempotent.
/// </remarks>
public sealed class OutboxBackgroundService : BackgroundService, IOutboxPublisher
{
    private readonly IServiceScopeFactory _scopes;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxBackgroundService> _logger;

    public OutboxBackgroundService(
        IServiceScopeFactory scopes,
        IOptions<OutboxOptions> options,
        ILogger<OutboxBackgroundService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox worker starting: poll={Poll}s batch={Batch} maxRetries={MaxRetry}.",
            _options.PollInterval.TotalSeconds, _options.BatchSize, _options.MaxRetryCount);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox drain loop failed; will retry next tick.");
            }

            try
            {
                await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("Outbox worker stopped.");
    }

    /// <inheritdoc />
    public async Task<int> DrainOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetService<IOutboxStore>();
        if (store is null)
        {
            _logger.LogDebug("IOutboxStore not registered; outbox worker idle.");
            return 0;
        }

        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var batch = await store.FetchPendingAsync(_options.BatchSize, _options.MaxRetryCount, ct).ConfigureAwait(false);
        if (batch.Count == 0) return 0;

        var ok = 0;
        foreach (var outboxEvent in batch)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await publisher.PublishRawAsync(outboxEvent.EventType, outboxEvent.Id, outboxEvent.Payload, ct).ConfigureAwait(false);
                await store.MarkPublishedAsync(outboxEvent.Id, DateTime.UtcNow, ct).ConfigureAwait(false);
                ok++;
            }
            catch (Exception ex)
            {
                var nextRetry = outboxEvent.RetryCount + 1;
                var giveUp = nextRetry > _options.MaxRetryCount;
                var backoff = ComputeBackoff(nextRetry);
                var nextAt = giveUp
                    ? DateTime.UtcNow.AddYears(100) // park indefinitely; ops to inspect
                    : DateTime.UtcNow.Add(backoff);

                _logger.Log(
                    giveUp ? LogLevel.Error : LogLevel.Warning,
                    ex,
                    "Outbox publish failed for {EventType} id={Id} attempt={Attempt}/{Max}. NextAttempt={NextAt}.",
                    outboxEvent.EventType, outboxEvent.Id, nextRetry, _options.MaxRetryCount, nextAt);

                if (giveUp)
                {
                    await store.MoveToDlqAsync(outboxEvent.Id, ex.Message, DateTime.UtcNow, ct).ConfigureAwait(false);
                }
                else
                {
                    await store.MarkFailedAsync(outboxEvent.Id, ex.Message, nextAt, ct).ConfigureAwait(false);
                }
            }
        }

        if (ok > 0)
            _logger.LogInformation("Outbox drained {Count}/{Total} message(s).", ok, batch.Count);

        return ok;
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        // attempt 1 -> base; doubles each retry, capped at max.
        var baseSec = _options.BackoffBaseDelay.TotalSeconds;
        var seconds = baseSec * Math.Pow(2, Math.Max(0, attempt - 1));
        if (seconds > _options.BackoffMaxDelay.TotalSeconds)
            seconds = _options.BackoffMaxDelay.TotalSeconds;
        return TimeSpan.FromSeconds(seconds);
    }
}
