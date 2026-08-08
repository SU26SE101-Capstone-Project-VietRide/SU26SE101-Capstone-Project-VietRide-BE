namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public sealed record PaymentOperatorRevenueSummaryDto(
    long GrossParcelRevenueVnd,
    long ParcelRefundsVnd,
    long NetParcelRevenueVnd);
