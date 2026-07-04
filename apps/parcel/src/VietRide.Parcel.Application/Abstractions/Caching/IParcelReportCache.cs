namespace VietRide.Parcel.Application.Abstractions.Caching;

public interface IParcelReportCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken);
}
