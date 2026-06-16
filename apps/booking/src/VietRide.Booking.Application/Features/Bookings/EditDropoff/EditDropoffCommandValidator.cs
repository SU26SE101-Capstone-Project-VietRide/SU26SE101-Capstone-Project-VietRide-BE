using FluentValidation;

namespace VietRide.Booking.Application.Features.Bookings.EditDropoff;

/// <summary>
/// Input-shape validation for <see cref="EditDropoffCommand"/>.
/// Business checks (owner, cutoff, route-stop flags/order) live in the handler.
/// </summary>
public sealed class EditDropoffCommandValidator : AbstractValidator<EditDropoffCommand>
{
    public EditDropoffCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty();

        RuleFor(x => x.PassengerUserId)
            .NotEmpty();

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty();

        RuleFor(x => x)
            .Must(x => CountProvided(x.DropoffStationId, x.DropoffStopId) == 1)
            .WithName("dropoff")
            .WithMessage("Exactly one of dropoffStationId or dropoffStopId must be provided.");
    }

    private static int CountProvided(Guid? first, Guid? second)
        => (first.HasValue ? 1 : 0) + (second.HasValue ? 1 : 0);
}
