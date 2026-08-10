using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips.SearchTrips;

public sealed class SearchTripsValidator : AbstractValidator<SearchTripsQuery>
{
    public SearchTripsValidator()
    {
        RuleFor(query => query)
            .Must(HaveStationPairOrLocationPair)
            .WithMessage("Provide both station ids or both province codes.");
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
        RuleFor(query => query.OriginProvinceCode)
            .Length(2)
            .MaximumLength(20)
            .Matches("^[0-9]+$")
            .When(query => !HasStationPair(query) && !string.IsNullOrWhiteSpace(query.OriginProvinceCode));
        RuleFor(query => query.OriginWardCode)
            .Length(5)
            .MaximumLength(20)
            .Matches("^[0-9]+$")
            .When(query => !HasStationPair(query) && !string.IsNullOrWhiteSpace(query.OriginWardCode));
        RuleFor(query => query.DestinationProvinceCode)
            .Length(2)
            .MaximumLength(20)
            .Matches("^[0-9]+$")
            .When(query => !HasStationPair(query) && !string.IsNullOrWhiteSpace(query.DestinationProvinceCode));
        RuleFor(query => query.DestinationWardCode)
            .Length(5)
            .MaximumLength(20)
            .Matches("^[0-9]+$")
            .When(query => !HasStationPair(query) && !string.IsNullOrWhiteSpace(query.DestinationWardCode));
        RuleFor(query => query.DepartureDate).NotEmpty();
        RuleFor(query => query.PassengerCount).GreaterThan(0);
    }

    private static bool HaveStationPairOrLocationPair(SearchTripsQuery query)
    {
        var hasStationPair = HasStationPair(query);
        var hasLocationPair = !query.OriginStationId.HasValue
            && !query.DestinationStationId.HasValue
            && !string.IsNullOrWhiteSpace(query.OriginProvinceCode)
            && !string.IsNullOrWhiteSpace(query.DestinationProvinceCode);

        return hasStationPair || hasLocationPair;
    }

    private static bool HasStationPair(SearchTripsQuery query)
        => query.OriginStationId.HasValue && query.DestinationStationId.HasValue;
}
