using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Parcel.Application.Features.Parcels.Reports;

internal static class ParcelReportQuerySupport
{
    public static (DateOnly From, DateOnly To) NormalizeRange(DateOnly? from, DateOnly? to, IClock clock)
    {
        var today = BusinessTime.ToLocalDate(clock.UtcNow);
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
        IPaymentOperatorRevenueSummaryClient paymentRevenue,
        Guid operatorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var stats = await statsRepository.QueryNoTracking()
            .Where(stat => stat.OperatorId == operatorId && stat.StatDate >= from && stat.StatDate <= to)
            .ToListAsync(cancellationToken);

        int totalParcels;
        int totalLoaded;
        int totalDelivered;
        int totalRejected;
        int totalReturned;
        string source;

        if (stats.Count > 0)
        {
            totalParcels = stats.Sum(stat => stat.TotalParcels);
            totalLoaded = stats.Sum(stat => stat.TotalLoaded);
            totalDelivered = stats.Sum(stat => stat.TotalDelivered);
            totalRejected = stats.Sum(stat => stat.TotalRejected);
            totalReturned = stats.Sum(stat => stat.TotalReturned);
            source = "ParcelStats";
        }

        else
        {
            var fromDateTime = ToUtc(from);
            var toDateTime = ToUtc(to.AddDays(1));
            var parcels = await parcelRepository.QueryNoTracking()
                .Where(parcel => parcel.OperatorId == operatorId
                    && parcel.CreatedAt >= fromDateTime
                    && parcel.CreatedAt < toDateTime)
                .Select(parcel => parcel.Status)
                .ToListAsync(cancellationToken);

            totalParcels = parcels.Count;
            totalLoaded = parcels.Count(status => status is ParcelStatus.LOADED or ParcelStatus.IN_TRANSIT
                or ParcelStatus.DELIVERED_PENDING_CONFIRM or ParcelStatus.DELIVERY_CONFIRMED);
            totalDelivered = parcels.Count(status => status == ParcelStatus.DELIVERY_CONFIRMED);
            totalRejected = parcels.Count(status => status == ParcelStatus.DELIVERY_REJECTED);
            totalReturned = parcels.Count(status => status == ParcelStatus.RETURNED);
            source = "ParcelsFallback";
        }

        var money = await paymentRevenue.GetAsync(operatorId, from, to, cancellationToken);

        return new ParcelReportSummaryResponse(
            operatorId,
            from,
            to,
            totalParcels,
            totalLoaded,
            totalDelivered,
            totalRejected,
            totalReturned,
            money.GrossParcelRevenueVnd,
            money.ParcelRefundsVnd,
            money.NetParcelRevenueVnd,
            source);
    }

    private static DateTimeOffset ToUtc(DateOnly date)
    {
        return BusinessTime.ToUtc(date, TimeOnly.MinValue);
    }
}
