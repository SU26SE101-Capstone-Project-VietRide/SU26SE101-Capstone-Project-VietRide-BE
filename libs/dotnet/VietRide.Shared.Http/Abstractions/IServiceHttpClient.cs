namespace VietRide.Shared.Http.Abstractions;

/// <summary>
/// Marker interface implemented by every typed inter-service HTTP client
/// (e.g. <c>ITripServiceClient</c>, <c>IIdentityServiceClient</c>).
/// Used by <c>HttpServiceCollectionExtensions.AddVietRideServiceClient</c>
/// to apply the standard delegating-handler pipeline
/// (Internal JWT + correlation id + Polly retry + circuit breaker).
/// </summary>
public interface IServiceHttpClient
{
}
