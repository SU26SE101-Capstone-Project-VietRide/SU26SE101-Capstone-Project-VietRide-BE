namespace VietRide.Parcel.Application.Features.Internal.Reports.PlatformParcels;

public sealed record PlatformParcelReportItem(
    Guid OperatorId,
    long DeliveredParcelCount,
    long ParcelRevenueVnd);
