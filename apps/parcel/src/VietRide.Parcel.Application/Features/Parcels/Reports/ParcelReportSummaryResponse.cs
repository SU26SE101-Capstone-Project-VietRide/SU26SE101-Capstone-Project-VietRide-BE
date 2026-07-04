namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public sealed record ParcelReportSummaryResponse(
    Guid OperatorId,
    DateOnly From,
    DateOnly To,
    int TotalParcels,
    int TotalLoaded,
    int TotalDelivered,
    int TotalRejected,
    int TotalReturned,
    long TotalRevenue,
    long TotalRefunded,
    string Source);
