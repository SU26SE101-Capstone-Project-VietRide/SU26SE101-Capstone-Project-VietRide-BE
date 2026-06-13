using FluentValidation;

namespace VietRide.Booking.Application.Features.Bookings.EditPickup;

/// <summary>
/// Input-shape validation for <see cref="EditPickupCommand"/>.
/// Business checks (owner, cutoff, fare equality) live in the handler.
/// </summary>
public sealed class EditPickupCommandValidator : AbstractValidator<EditPickupCommand>
{
    private static readonly string[] ValidPaymentMethods = ["WALLET", "VNPAY"];

    public EditPickupCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty();

        RuleFor(x => x.PassengerUserId)
            .NotEmpty();

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty();

        RuleFor(x => x)
            .Must(x => CountProvided(x.PickupStationId, x.PickupStopId) == 1)
            .WithName("pickup")
            .WithMessage("Exactly one of pickupStationId or pickupStopId must be provided.");

        RuleFor(x => x.PaymentMethod)
            .NotEmpty()
            .Must(m => ValidPaymentMethods.Contains(m, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"paymentMethod must be one of: {string.Join(", ", ValidPaymentMethods)}.");
    }

    private static int CountProvided(Guid? first, Guid? second)
        => (first.HasValue ? 1 : 0) + (second.HasValue ? 1 : 0);
}
