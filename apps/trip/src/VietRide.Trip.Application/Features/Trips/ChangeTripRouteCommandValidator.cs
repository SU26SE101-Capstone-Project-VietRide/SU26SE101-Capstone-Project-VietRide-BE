using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips;

public sealed class ChangeTripRouteCommandValidator : AbstractValidator<ChangeTripRouteCommand>
{
    public ChangeTripRouteCommandValidator()
    {
        RuleFor(command => command.TripId).NotEmpty();
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command.AlternativeRouteId).NotEmpty();
    }
}
