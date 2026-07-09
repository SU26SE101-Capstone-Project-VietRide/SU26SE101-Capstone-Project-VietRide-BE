namespace VietRide.Parcel.Application.Abstractions.Repositories;

public interface IParcelPricingPolicyRepository
{
    Task<decimal> GetSystemDecimalAsync(string key, decimal fallback, DateTimeOffset now, CancellationToken cancellationToken);

    Task<decimal> GetDepositPercentAsync(
        Guid operatorId,
        Guid routeId,
        decimal fallback,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
