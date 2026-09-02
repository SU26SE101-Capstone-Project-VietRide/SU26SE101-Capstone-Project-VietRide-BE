using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public sealed record ParcelOperatorReportRow(
    Guid ParcelId,
    string ParcelCode,
    Guid TripId,
    string? RouteName,
    string? OriginStationName,
    string? DestinationStationName,
    string? VehicleLicensePlate,
    ParcelStatus Status,
    ParcelSizeCategory SizeCategory,
    long TotalPriceVnd,
    long DepositAmountVnd,
    long AdditionalAmountVnd,
    long RefundAmountVnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt);
