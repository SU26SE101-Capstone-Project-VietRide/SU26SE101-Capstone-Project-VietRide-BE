namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public sealed record ParcelOperatorReportRow(
    Guid ParcelId,
    string ParcelCode,
    Guid TripId,
    string Status,
    string SizeCategory,
    long TotalPriceVnd,
    long DepositAmountVnd,
    long AdditionalAmountVnd,
    long RefundAmountVnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt);
