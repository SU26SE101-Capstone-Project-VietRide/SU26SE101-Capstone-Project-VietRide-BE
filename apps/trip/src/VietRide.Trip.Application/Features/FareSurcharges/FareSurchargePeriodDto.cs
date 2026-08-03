using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed record FareSurchargePeriodDto(
    Guid PeriodId,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    int SurchargePercent,
    bool IsActive,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static FareSurchargePeriodDto FromEntity(OperatorFareSurchargePeriod entity, DateOnly today)
        => new(
            entity.Id,
            entity.Name,
            entity.StartDate,
            entity.EndDate,
            entity.SurchargePercent,
            entity.IsActive,
            ResolveStatus(entity, today),
            entity.CreatedAt,
            entity.UpdatedAt);

    private static string ResolveStatus(OperatorFareSurchargePeriod entity, DateOnly today)
    {
        if (!entity.IsActive)
            return "DISABLED";
        if (today < entity.StartDate)
            return "UPCOMING";
        if (today > entity.EndDate)
            return "EXPIRED";
        return "APPLYING";
    }
}
