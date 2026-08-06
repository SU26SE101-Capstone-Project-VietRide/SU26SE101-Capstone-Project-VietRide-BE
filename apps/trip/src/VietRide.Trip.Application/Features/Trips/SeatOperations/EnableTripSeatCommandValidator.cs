using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips.SeatOperations;

public sealed class EnableTripSeatCommandValidator : AbstractValidator<EnableTripSeatCommand>
{
    public EnableTripSeatCommandValidator()
    {
        RuleFor(command => command.TripId).NotEmpty();
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command.SeatNumber)
            .NotEmpty()
            .MaximumLength(20);
        RuleFor(command => command.RequestId).NotEmpty().MaximumLength(200);
    }
}
