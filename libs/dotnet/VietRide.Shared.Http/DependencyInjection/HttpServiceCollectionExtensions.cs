using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Shared.Http.Abstractions;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Http.Resilience;

namespace VietRide.Shared.Http.DependencyInjection;

/// <summary>
/// Composition-root helpers for VietRide.Shared.Http.
/// </summary>
/// <remarks>
/// Typical usage in a service's <c>Program.cs</c>:
/// <code>
/// services.AddHttpContextAccessor();
/// services.AddSingleton&lt;IInternalJwtTokenProvider, InternalJwtTokenProvider&gt;(); // from Shared.Web
/// services.AddVietRideServiceClient&lt;ITripServiceClient, TripServiceClient&gt;("TRIP_SERVICE_BASE_URL");
/// </code>
/// The supplied env var must contain an absolute base URL
/// (e.g. <c>http://trip:5002</c>). Each call attaches the standard
/// delegating-handler pipeline (Internal JWT + correlation id) and Polly
/// retry + circuit breaker.
/// </remarks>
public static class HttpServiceCollectionExtensions
{
    /// <summary>
    /// Registers a typed inter-service HTTP client with the standard
    /// VietRide pipeline: Internal JWT signer, correlation id propagation,
    /// retry policy, circuit breaker.
    /// </summary>
    /// <typeparam name="TClient">Interface — must derive
    /// <see cref="IServiceHttpClient"/>.</typeparam>
    /// <typeparam name="TImpl">Concrete implementation.</typeparam>
    /// <param name="services">DI container.</param>
    /// <param name="baseAddressEnvKey">Environment variable name holding
    /// the absolute base URL (e.g. <c>TRIP_SERVICE_BASE_URL</c>).</param>
    /// <returns>
    /// The <see cref="IHttpClientBuilder"/> so callers can chain extra
    /// configuration (e.g. extra delegating handlers, named options).
    /// </returns>
    public static IHttpClientBuilder AddVietRideServiceClient<TClient, TImpl>(
        this IServiceCollection services,
        string baseAddressEnvKey)
        where TClient : class, IServiceHttpClient
        where TImpl : class, TClient
    {
        if (string.IsNullOrWhiteSpace(baseAddressEnvKey))
            throw new ArgumentException("Base address env key required.", nameof(baseAddressEnvKey));

        // Ensure HttpContextAccessor is registered for the delegating handlers.
        services.AddHttpContextAccessor();
        services.AddTransient<InternalJwtDelegatingHandler>();
        services.AddTransient<CorrelationIdDelegatingHandler>();

        return services
            .AddHttpClient<TClient, TImpl>(client =>
            {
                var baseUrl = Environment.GetEnvironmentVariable(baseAddressEnvKey)
                    ?? throw new InvalidOperationException(
                        $"Environment variable '{baseAddressEnvKey}' is not set; cannot " +
                        $"resolve base address for {typeof(TClient).Name}.");
                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddHttpMessageHandler<InternalJwtDelegatingHandler>()
            .AddPolicyHandler(HttpResiliencePolicies.GetRetryPolicy())
            .AddPolicyHandler(HttpResiliencePolicies.GetCircuitBreakerPolicy());
    }
}
