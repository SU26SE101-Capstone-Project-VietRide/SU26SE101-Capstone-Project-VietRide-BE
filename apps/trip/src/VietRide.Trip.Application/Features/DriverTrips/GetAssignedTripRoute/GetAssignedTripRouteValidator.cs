using FluentValidation;

namespace VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;

public sealed class GetAssignedTripRouteValidator : AbstractValidator<GetAssignedTripRouteQuery>
{
    public GetAssignedTripRouteValidator()
    {
        RuleFor(query => query.TripId).NotEmpty().WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.UserId).NotEmpty().WithErrorCode("VALIDATION_ERROR");
    }
}
