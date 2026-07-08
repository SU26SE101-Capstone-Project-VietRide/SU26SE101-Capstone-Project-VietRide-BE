using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips.SearchTrips;

public sealed class SearchTripsValidator : AbstractValidator<SearchTripsQuery>
{
    public SearchTripsValidator()
    {
        RuleFor(query => query)
            .Must(HaveStationPairOrLocationPair)
            .WithMessage("Provide origin/destination station ids or origin/destination location codes.");
        RuleFor(query => query.OriginStationId)
            .Must(stationId => stationId != Guid.Empty)
            .WithMessage("OriginStationId must be valid.")
            .When(query => query.OriginStationId.HasValue);
        RuleFor(query => query.DestinationStationId)
            .Must(stationId => stationId != Guid.Empty)
            .WithMessage("DestinationStationId must be valid.")
            .When(query => query.DestinationStationId.HasValue);
        RuleFor(query => query)
            .Must(query => !query.OriginStationId.HasValue
                || !query.DestinationStationId.HasValue
                || query.OriginStationId.Value != query.DestinationStationId.Value)
            .WithMessage("DestinationStationId must differ from OriginStationId.");
        RuleFor(query => query.OriginLocationCode)
            .MaximumLength(20)
            .When(query => !string.IsNullOrWhiteSpace(query.OriginLocationCode));
        RuleFor(query => query.DestinationLocationCode)
            .MaximumLength(20)
            .When(query => !string.IsNullOrWhiteSpace(query.DestinationLocationCode));
        RuleFor(query => query.DepartureDate).NotEmpty();
        RuleFor(query => query.PassengerCount).GreaterThan(0);
    }

    private static bool HaveStationPairOrLocationPair(SearchTripsQuery query)
    {
        var hasStationPair = query.OriginStationId.HasValue && query.DestinationStationId.HasValue;
        var hasLocationPair = !string.IsNullOrWhiteSpace(query.OriginLocationCode)
            && !string.IsNullOrWhiteSpace(query.DestinationLocationCode);

        return hasStationPair || hasLocationPair;
    }
}
