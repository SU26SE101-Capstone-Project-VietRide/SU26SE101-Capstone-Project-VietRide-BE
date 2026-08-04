namespace VietRide.Trip.Application.Abstractions.Services;

public interface IFareSurchargeService
{
    Task<FareSurchargeRule?> ResolveAsync(
        Guid operatorId,
        DateTimeOffset departureDateTime,
        CancellationToken cancellationToken = default);

    FareSurchargeAdjustment Apply(long originalFare, FareSurchargeRule? rule);
}
