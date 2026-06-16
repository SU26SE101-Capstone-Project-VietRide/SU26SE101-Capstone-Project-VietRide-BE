using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips.SearchTrips;

public sealed class SearchTripsValidator : AbstractValidator<SearchTripsQuery>
{
    public SearchTripsValidator()
    {
        RuleFor(query => query.OriginStationId).NotEmpty();
        RuleFor(query => query.DestinationStationId).NotEmpty();
        RuleFor(query => query.DestinationStationId)
            .NotEqual(query => query.OriginStationId)
            .WithMessage("DestinationStationId must differ from OriginStationId.");
        RuleFor(query => query.DepartureDate).NotEmpty();
        RuleFor(query => query.PassengerCount).GreaterThan(0);
    }
}
