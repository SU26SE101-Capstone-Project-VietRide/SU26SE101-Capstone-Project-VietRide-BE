using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Parcels.Quotes;

public sealed record ParcelQuote(
    ParcelCargoEstimate Cargo,
    ParcelSizeCategory SizeCategory,
    ParcelRouteFare Fare,
    long EstimatedGrossPriceVnd,
    long EstimatedDiscountVnd,
    long EstimatedTotalPriceVnd,
    decimal DepositPercent,
    long EstimatedDepositVnd,
    decimal DimWeightFactor);

public sealed record IssuedParcelQuote(string Token, DateTimeOffset ExpiresAt);

public sealed record ParcelQuoteTokenExpectation(
    Guid SenderUserId,
    Guid TripId,
    Guid RouteId,
    Guid OperatorId,
    Guid? OriginStationId = null,
    Guid? DestinationStationId = null,
    decimal? LengthCm = null,
    decimal? WidthCm = null,
    decimal? HeightCm = null,
    decimal? WeightKg = null,
    ParcelSizeCategory? SizeCategory = null);
