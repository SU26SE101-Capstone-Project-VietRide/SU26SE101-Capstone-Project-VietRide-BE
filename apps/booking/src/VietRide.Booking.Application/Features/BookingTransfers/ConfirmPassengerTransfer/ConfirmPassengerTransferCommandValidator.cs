using FluentValidation;

namespace VietRide.Booking.Application.Features.BookingTransfers.ConfirmPassengerTransfer;

public sealed class ConfirmPassengerTransferCommandValidator
    : AbstractValidator<ConfirmPassengerTransferCommand>
{
    public ConfirmPassengerTransferCommandValidator()
    {
        RuleFor(command => command.NewTripId).NotEmpty();
        RuleFor(command => command.PassengerId).NotEmpty();
        RuleFor(command => command.CallerUserId).NotEmpty();
    }
}
