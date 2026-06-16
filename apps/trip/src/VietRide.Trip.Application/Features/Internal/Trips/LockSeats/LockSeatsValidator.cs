using FluentValidation;

namespace VietRide.Trip.Application.Features.Internal.Trips.LockSeats;

public sealed class LockSeatsValidator : AbstractValidator<LockSeatsCommand>
{
    public LockSeatsValidator()
    {
        RuleFor(command => command.TripId).NotEmpty();
        RuleFor(command => command.HoldOwnerId).NotEmpty();
        RuleFor(command => command.SeatNumbers).NotEmpty();
        RuleForEach(command => command.SeatNumbers).NotEmpty().MaximumLength(20);
        RuleFor(command => command.TtlSeconds).GreaterThan(0).When(command => command.TtlSeconds.HasValue);
    }
}
