using Polly;
using Polly.Extensions.Http;

namespace VietRide.Shared.Http.Resilience;

/// <summary>
/// Static factory of Polly <see cref="IAsyncPolicy{HttpResponseMessage}"/>
/// instances used by every inter-service HTTP client.
/// </summary>
/// <remarks>
/// Conventions:
/// <list type="bullet">
///   <item><description><see cref="GetRetryPolicy"/> — 3 retries, exponential
///   backoff with jitter (2^attempt seconds + 0-250ms random). Handles
///   HttpRequestException + 5xx + 408.</description></item>
///   <item><description><see cref="GetCircuitBreakerPolicy"/> — opens after
///   5 consecutive failures for 30 seconds.</description></item>
/// </list>
/// Both policies are intended to be registered once per typed client via
/// <c>HttpServiceCollectionExtensions.AddVietRideServiceClient</c>.
/// </remarks>
public static class HttpResiliencePolicies
{
    private static readonly Random JitterSource = new();
    private static readonly object JitterLock = new();

    /// <summary>Default retry count (excluding the initial attempt).</summary>
    public const int DefaultRetryCount = 3;

    /// <summary>Default break duration when the circuit trips.</summary>
    public static readonly TimeSpan DefaultBreakDuration = TimeSpan.FromSeconds(30);

    /// <summary>Default consecutive-failure threshold before the circuit opens.</summary>
    public const int DefaultFailuresBeforeBreak = 5;

    /// <summary>
    /// 3 retries with exponential backoff + jitter. Treats transient HTTP
    /// errors (5xx, 408) and <see cref="HttpRequestException"/> as retryable.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(
        int retryCount = DefaultRetryCount)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // 5xx + 408 + HttpRequestException
            .WaitAndRetryAsync(
                retryCount: retryCount,
                sleepDurationProvider: ComputeBackoff);
    }

    /// <summary>
    /// Opens the circuit after <paramref name="failuresBeforeBreak"/>
    /// consecutive transient failures, keeps it open for
    /// <paramref name="breakDuration"/>.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
        int failuresBeforeBreak = DefaultFailuresBeforeBreak,
        TimeSpan? breakDuration = null)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: failuresBeforeBreak,
                durationOfBreak: breakDuration ?? DefaultBreakDuration);
    }

    private static TimeSpan ComputeBackoff(int attempt)
    {
        var seconds = Math.Pow(2, attempt);
        int jitterMs;
        lock (JitterLock) { jitterMs = JitterSource.Next(0, 250); }
        return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(jitterMs);
    }
}
