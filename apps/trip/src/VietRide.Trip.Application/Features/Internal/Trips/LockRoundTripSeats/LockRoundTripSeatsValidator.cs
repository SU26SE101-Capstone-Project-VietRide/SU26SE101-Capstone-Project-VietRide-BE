using FluentValidation;

namespace VietRide.Trip.Application.Features.Internal.Trips.LockRoundTripSeats;

public sealed class LockRoundTripSeatsValidator : AbstractValidator<LockRoundTripSeatsCommand>
{
    private const int MaxTtlSeconds = 1800;

    public LockRoundTripSeatsValidator()
    {
        RuleFor(command => command.Outbound.TripId)
            .NotEmpty();
        RuleFor(command => command.Return.TripId)
            .NotEmpty()
            .NotEqual(command => command.Outbound.TripId);
        RuleFor(command => command.Outbound.SeatNumbers)
            .NotEmpty();
        RuleForEach(command => command.Outbound.SeatNumbers)
            .NotEmpty();
        RuleFor(command => command.Return.SeatNumbers)
            .NotEmpty();
        RuleForEach(command => command.Return.SeatNumbers)
            .NotEmpty();
        RuleFor(command => command.HoldOwnerId)
            .NotEmpty();
        RuleFor(command => command.IdempotencyKey)
            .NotEmpty();
        RuleFor(command => command.TtlSeconds)
            .InclusiveBetween(1, MaxTtlSeconds)
            .When(command => command.TtlSeconds.HasValue);
    }
}
