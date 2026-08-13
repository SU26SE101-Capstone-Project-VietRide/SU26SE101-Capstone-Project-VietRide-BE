using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.Security;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Application.Features.Parcels.Quotes;

public sealed class ParcelQuoteService
{
    public const int TokenVersion = 1;
    public const int DimPolicyVersion = 1;

    private readonly IParcelRouteFareRepository _fareRepository;
    private readonly IParcelPricingPolicyRepository? _policyRepository;
    private readonly IParcelQuoteTokenService? _tokenService;
    private readonly int _tokenTtlSeconds;

    public ParcelQuoteService(
        IParcelRouteFareRepository fareRepository,
        IParcelPricingPolicyRepository? policyRepository = null,
        IParcelQuoteTokenService? tokenService = null,
        int tokenTtlSeconds = 600)
    {
        _fareRepository = fareRepository;
        _policyRepository = policyRepository;
        _tokenService = tokenService;
        _tokenTtlSeconds = tokenTtlSeconds > 0 ? tokenTtlSeconds : 600;
    }

    public async Task<ParcelCargoEstimate> CalculateCargoAsync(
        decimal lengthCm,
        decimal widthCm,
        decimal heightCm,
        decimal weightKg,
        DateTimeOffset pricingAt,
        CancellationToken cancellationToken)
    {
        var dimFactor = await GetDimWeightFactorAsync(pricingAt, cancellationToken);
        return ParcelCargoCalculator.Calculate(lengthCm, widthCm, heightCm, weightKg, dimFactor);
    }

    public async Task<decimal> GetDimWeightFactorAsync(
        DateTimeOffset pricingAt,
        CancellationToken cancellationToken)
        => _policyRepository is null
            ? ParcelCargoCalculator.DefaultDimWeightFactor
            : await _policyRepository.GetSystemDecimalAsync(
                "DIM_WEIGHT_FACTOR",
                ParcelCargoCalculator.DefaultDimWeightFactor,
                pricingAt,
                cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListEligibleRouteIdsAsync(
        ParcelSizeCategory sizeCategory,
        DateTimeOffset pricingAt,
        CancellationToken cancellationToken)
    {
        var query = _fareRepository.QueryNoTracking()
            .Where(fare => fare.SizeCategory == sizeCategory
                && fare.EffectiveFrom <= pricingAt
                && (fare.EffectiveUntil == null || pricingAt < fare.EffectiveUntil))
            .Select(fare => fare.RouteId)
            .Distinct();
        return query.Provider is IAsyncQueryProvider
            ? await query.ToListAsync(cancellationToken)
            : query.ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, ParcelRouteFare>> LoadActiveFaresAsync(
        IReadOnlyCollection<Guid> routeIds,
        ParcelSizeCategory sizeCategory,
        DateTimeOffset pricingAt,
        CancellationToken cancellationToken)
    {
        if (routeIds.Count == 0)
            return new Dictionary<Guid, ParcelRouteFare>();

        var distinctRouteIds = routeIds.Distinct().ToArray();
        var query = _fareRepository.QueryNoTracking()
            .Where(fare => distinctRouteIds.Contains(fare.RouteId)
                && fare.SizeCategory == sizeCategory
                && fare.EffectiveFrom <= pricingAt
                && (fare.EffectiveUntil == null || pricingAt < fare.EffectiveUntil));
        return query.Provider is IAsyncQueryProvider
            ? await query.ToDictionaryAsync(fare => fare.RouteId, cancellationToken)
            : query.ToDictionary(fare => fare.RouteId);
    }

    public async Task<ParcelRouteFare?> FindActiveFareAsync(
        Guid routeId,
        ParcelSizeCategory sizeCategory,
        DateTimeOffset pricingAt,
        CancellationToken cancellationToken)
    {
        var fare = await _fareRepository.FindByCompositeAsync(routeId, sizeCategory, cancellationToken);
        return fare is not null
            && fare.EffectiveFrom <= pricingAt
            && (fare.EffectiveUntil is null || pricingAt < fare.EffectiveUntil)
                ? fare
                : null;
    }

    public ParcelQuote Calculate(
        ParcelCargoEstimate cargo,
        ParcelRouteFare fare,
        long discountAmountVnd,
        decimal dimWeightFactor)
    {
        var sizeCategory = ParcelCargoCalculator.DeriveSizeCategory(cargo.ChargeableWeightKg);
        if (fare.SizeCategory != sizeCategory)
        {
            throw new InvalidOperationException("The fare does not match the derived parcel size category.");
        }

        var pricePerKg = fare.PricePerChargeableKgVnd.Amount > 0
            ? fare.PricePerChargeableKgVnd
            : fare.PriceVnd;
        var gross = ParcelCargoCalculator.CalculateTotalPrice(
            cargo.ChargeableWeightKg,
            pricePerKg,
            fare.MinimumPriceVnd);
        var discount = Money.FromRaw(Math.Max(0, discountAmountVnd));
        var total = ParcelCargoCalculator.CalculateDiscountedTotal(gross, discount);
        var depositPercent = ParcelCargoCalculator.DefaultDepositPercent;
        var deposit = ParcelCargoCalculator.CalculatePercent(total, depositPercent);

        return new ParcelQuote(
            cargo,
            sizeCategory,
            fare,
            gross.Amount,
            Math.Min(discount.Amount, gross.Amount),
            total.Amount,
            depositPercent,
            deposit.Amount,
            dimWeightFactor);
    }

    public IssuedParcelQuote? IssueToken(
        ParcelQuote quote,
        Guid senderUserId,
        Guid tripId,
        Guid routeId,
        Guid operatorId,
        Guid originStationId,
        Guid destinationStationId,
        DateTimeOffset issuedAt)
    {
        if (_tokenService is null || senderUserId == Guid.Empty)
            return null;

        var expiresAt = issuedAt.AddSeconds(_tokenService.TtlSeconds > 0
            ? _tokenService.TtlSeconds
            : _tokenTtlSeconds);
        var payload = new ParcelQuoteTokenPayload(
            TokenVersion,
            senderUserId,
            tripId,
            routeId,
            operatorId,
            originStationId,
            destinationStationId,
            quote.Cargo.LengthCm,
            quote.Cargo.WidthCm,
            quote.Cargo.HeightCm,
            quote.Cargo.WeightKg,
            quote.Cargo.VolumeM3,
            quote.Cargo.DimWeightKg,
            quote.Cargo.ChargeableWeightKg,
            quote.SizeCategory.ToString(),
            quote.Fare.Id,
            quote.Fare.UpdatedAt,
            quote.Fare.EffectiveFrom,
            quote.Fare.EffectiveUntil,
            quote.Fare.PricePerChargeableKgVnd.Amount > 0
                ? quote.Fare.PricePerChargeableKgVnd.Amount
                : quote.Fare.PriceVnd.Amount,
            quote.Fare.MinimumPriceVnd.Amount,
            quote.DimWeightFactor,
            DimPolicyVersion,
            ParcelCargoCalculator.SettlementPolicyVersion,
            quote.EstimatedGrossPriceVnd,
            quote.EstimatedDiscountVnd,
            quote.DepositPercent,
            quote.EstimatedDepositVnd,
            issuedAt,
            expiresAt,
            Guid.NewGuid());

        return new IssuedParcelQuote(_tokenService.Issue(payload), expiresAt);
    }

    public async Task<ParcelQuoteTokenPayload> ValidateTokenAsync(
        string token,
        ParcelQuoteTokenExpectation expectation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_tokenService is null)
            throw Conflict("PARCEL_QUOTE_INVALID", "Parcel quote token validation is unavailable.");

        var read = _tokenService.Read(token, now);
        if (read.Kind == ParcelQuoteTokenReadOutcomeKind.Invalid || read.Payload is null)
            throw Conflict("PARCEL_QUOTE_INVALID", "Parcel quote token is invalid.");
        if (read.Kind == ParcelQuoteTokenReadOutcomeKind.Expired)
            throw Conflict("PARCEL_QUOTE_EXPIRED", "Parcel quote token has expired.");

        var payload = read.Payload;
        if (!Enum.TryParse<ParcelSizeCategory>(payload.SizeCategory, out var category)
            || !Enum.IsDefined(category))
        {
            throw Conflict("PARCEL_QUOTE_INVALID", "Parcel quote token has an invalid size category.");
        }

        if (payload.Version != TokenVersion
            || payload.SenderUserId != expectation.SenderUserId
            || payload.TripId != expectation.TripId
            || payload.RouteId != expectation.RouteId
            || payload.OperatorId != expectation.OperatorId
            || (expectation.OriginStationId.HasValue && payload.OriginStationId != expectation.OriginStationId)
            || (expectation.DestinationStationId.HasValue && payload.DestinationStationId != expectation.DestinationStationId)
            || (expectation.LengthCm.HasValue && payload.LengthCm != Normalize(expectation.LengthCm.Value))
            || (expectation.WidthCm.HasValue && payload.WidthCm != Normalize(expectation.WidthCm.Value))
            || (expectation.HeightCm.HasValue && payload.HeightCm != Normalize(expectation.HeightCm.Value))
            || (expectation.WeightKg.HasValue && payload.WeightKg != Normalize(expectation.WeightKg.Value))
            || (expectation.SizeCategory.HasValue && category != expectation.SizeCategory.Value))
        {
            throw Conflict("PARCEL_QUOTE_MISMATCH", "Parcel request does not match the server quote.");
        }

        var currentDimFactor = await GetDimWeightFactorAsync(now, cancellationToken);
        if (payload.DimPolicyVersion != DimPolicyVersion
            || payload.SettlementPolicyVersion != ParcelCargoCalculator.SettlementPolicyVersion
            || payload.DimWeightFactor != currentDimFactor)
        {
            throw Conflict("PARCEL_QUOTE_STALE", "Parcel pricing policy changed; request a new quote.");
        }

        var fare = await FindActiveFareAsync(payload.RouteId, category, now, cancellationToken);
        var pricePerKg = fare is null
            ? 0
            : fare.PricePerChargeableKgVnd.Amount > 0
                ? fare.PricePerChargeableKgVnd.Amount
                : fare.PriceVnd.Amount;
        if (fare is null
            || fare.Id != payload.FareId
            || fare.UpdatedAt != payload.FareUpdatedAt
            || fare.EffectiveFrom != payload.FareEffectiveFrom
            || fare.EffectiveUntil != payload.FareEffectiveUntil
            || pricePerKg != payload.PricePerChargeableKgVnd
            || fare.MinimumPriceVnd.Amount != payload.MinimumPriceVnd)
        {
            throw Conflict("PARCEL_QUOTE_STALE", "Parcel fare changed; request a new quote.");
        }

        return payload;
    }

    private static decimal Normalize(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static CodedConflictException Conflict(string code, string message)
        => new(code, message);
}
