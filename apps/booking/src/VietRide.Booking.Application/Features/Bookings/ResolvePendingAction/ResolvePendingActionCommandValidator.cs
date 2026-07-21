using FluentValidation;

namespace VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;

public sealed class ResolvePendingActionCommandValidator : AbstractValidator<ResolvePendingActionCommand>
{
    public ResolvePendingActionCommandValidator()
    {
        RuleFor(command => command.BookingId).NotEmpty();
        RuleFor(command => command.ActionId).NotEmpty();
        RuleFor(command => command.PassengerUserId).NotEmpty();
        RuleFor(command => command.IdempotencyKey)
            .Must(IsUuidV4)
            .WithMessage("Idempotency-Key must be a UUID v4.");
        RuleFor(command => command.Action)
            .NotEmpty()
            .Must(action => action is "ACCEPTED" or "REJECTED")
            .WithMessage("action must be ACCEPTED or REJECTED.");
        RuleFor(command => command.ExtraFields)
            .Empty()
            .WithMessage("Request contains unsupported fields.");
    }

    private static bool IsUuidV4(string? value)
        => Guid.TryParse(value, out var parsed)
            && parsed != Guid.Empty
            && parsed.ToString("D")[14] == '4';
}
