using FluentValidation;

namespace VietRide.Booking.Application.Features.Boarding.TickPassengerBoarded;

public sealed class TickPassengerBoardedCommandValidator
    : AbstractValidator<TickPassengerBoardedCommand>
{
    public TickPassengerBoardedCommandValidator()
    {
        RuleFor(command => command.TripId).NotEmpty();
        RuleFor(command => command.PassengerRecordId).NotEmpty();
        RuleFor(command => command.CallerUserId).NotEmpty();
    }
}
