using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Services;

public sealed class FareSurchargeService : IFareSurchargeService
{
    private static readonly TimeSpan IctOffset = TimeSpan.FromHours(7);
    private readonly IOperatorFareSurchargePeriodRepository _periods;
    private readonly IOperatorFareSurchargeSettingRepository _settings;

    public FareSurchargeService(
        IOperatorFareSurchargeSettingRepository settings,
        IOperatorFareSurchargePeriodRepository periods)
    {
        _settings = settings;
        _periods = periods;
    }

    public async Task<FareSurchargeRule?> ResolveAsync(
        Guid operatorId,
        DateTimeOffset departureDateTime,
        CancellationToken cancellationToken = default)
    {
        var setting = await _settings.GetByOperatorIdAsync(operatorId, cancellationToken);
        if (setting?.IsEnabled != true)
            return null;

        var departureDate = DateOnly.FromDateTime(departureDateTime.ToOffset(IctOffset).DateTime);
        var period = await _periods.GetActiveForDateAsync(operatorId, departureDate, cancellationToken);
        return period is null
            ? null
            : new FareSurchargeRule(period.Id, period.Name, period.SurchargePercent);
    }

    public FareSurchargeAdjustment Apply(long originalFare, FareSurchargeRule? rule)
    {
        if (originalFare < 0)
            throw new ArgumentOutOfRangeException(nameof(originalFare));

        if (rule is null)
            return new FareSurchargeAdjustment(originalFare, 0, 0, originalFare, null, null);

        var effectiveFareDecimal = decimal.Round(
            originalFare * (100m + rule.Percent) / 100m,
            0,
            MidpointRounding.AwayFromZero);
        var effectiveFare = checked((long)effectiveFareDecimal);

        return new FareSurchargeAdjustment(
            originalFare,
            rule.Percent,
            checked(effectiveFare - originalFare),
            effectiveFare,
            rule.PeriodId,
            rule.PeriodName);
    }
}
