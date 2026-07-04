using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.Reports;

internal static class ParcelReportQuerySupport
{
    public static (DateOnly From, DateOnly To) NormalizeRange(DateOnly? from, DateOnly? to, IClock clock)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var normalizedTo = to ?? today;
        var normalizedFrom = from ?? normalizedTo.AddDays(-30);

        if (normalizedFrom > normalizedTo)
        {
            throw new ArgumentException("Report from date must be before or equal to to date.", nameof(from));
        }

        return (normalizedFrom, normalizedTo);
    }

    public static async Task<ParcelReportSummaryResponse> BuildSummaryAsync(
        IParcelStatsRepository statsRepository,
        IParcelRepository parcelRepository,
        Guid operatorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var stats = await statsRepository.QueryNoTracking()
            .Where(stat => stat.OperatorId == operatorId && stat.StatDate >= from && stat.StatDate <= to)
            .ToListAsync(cancellationToken);

        if (stats.Count > 0)
        {
            return new ParcelReportSummaryResponse(
                operatorId,
                from,
                to,
                stats.Sum(stat => stat.TotalParcels),
                stats.Sum(stat => stat.TotalLoaded),
                stats.Sum(stat => stat.TotalDelivered),
                stats.Sum(stat => stat.TotalRejected),
                stats.Sum(stat => stat.TotalReturned),
                stats.Sum(stat => stat.TotalRevenue),
                stats.Sum(stat => stat.TotalRefunded),
                "ParcelStats");
        }

        var fromDateTime = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var toDateTime = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var parcels = await parcelRepository.QueryNoTracking()
            .Where(parcel => parcel.OperatorId == operatorId
                && parcel.CreatedAt >= fromDateTime
                && parcel.CreatedAt < toDateTime)
            .Select(parcel => new
            {
                parcel.Status,
                DepositAmount = parcel.DepositAmount.Amount,
                AdditionalAmount = parcel.AdditionalAmount.Amount,
            })
            .ToListAsync(cancellationToken);

        return new ParcelReportSummaryResponse(
            operatorId,
            from,
            to,
            parcels.Count,
            parcels.Count(parcel => parcel.Status is ParcelStatus.LOADED or ParcelStatus.IN_TRANSIT
                or ParcelStatus.DELIVERED_PENDING_CONFIRM or ParcelStatus.DELIVERY_CONFIRMED),
            parcels.Count(parcel => parcel.Status == ParcelStatus.DELIVERY_CONFIRMED),
            parcels.Count(parcel => parcel.Status == ParcelStatus.DELIVERY_REJECTED),
            parcels.Count(parcel => parcel.Status == ParcelStatus.RETURNED),
            parcels.Sum(parcel => parcel.DepositAmount + parcel.AdditionalAmount),
            0,
            "ParcelsFallback");
    }
}
