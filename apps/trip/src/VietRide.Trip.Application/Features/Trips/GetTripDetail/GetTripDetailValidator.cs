using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips.GetTripDetail;

public sealed class GetTripDetailValidator : AbstractValidator<GetTripDetailQuery>
{
    public GetTripDetailValidator()
    {
        RuleFor(query => query.TripId).NotEmpty();
    }
}
