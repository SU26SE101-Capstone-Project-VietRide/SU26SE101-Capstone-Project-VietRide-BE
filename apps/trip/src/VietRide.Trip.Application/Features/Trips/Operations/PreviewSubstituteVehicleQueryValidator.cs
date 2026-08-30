using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class PreviewSubstituteVehicleQueryValidator : AbstractValidator<PreviewSubstituteVehicleQuery>
{
    public PreviewSubstituteVehicleQueryValidator()
    {
        RuleFor(query => query.TripId).NotEmpty();
        RuleFor(query => query.OperatorId).NotEmpty();
        RuleFor(query => query.ReplacementVehicleId).NotEmpty();
    }
}
