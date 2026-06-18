using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips.GetTripSeatMap;

public sealed class GetTripSeatMapValidator : AbstractValidator<GetTripSeatMapQuery>
{
    public GetTripSeatMapValidator()
    {
        RuleFor(query => query.TripId).NotEmpty();
    }
}
