using FluentValidation;

namespace VietRide.Booking.Application.Features.Bookings.CreateBooking;

/// <summary>Input-shape and field validation for <see cref="CreateBookingCommand"/>.</summary>
public sealed class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    private static readonly string[] ValidPaymentMethods = ["WALLET", "VNPAY"];

    public CreateBookingCommandValidator()
    {
        RuleFor(x => x.TripId)
            .NotEmpty();

        RuleFor(x => x.PassengerUserId)
            .NotEmpty();

        // Pickup: exactly one of StationId/StopId (application-level guard; domain also checks)
        RuleFor(x => x)
            .Must(x =>
            {
                var count = (x.PickupStationId.HasValue ? 1 : 0)
                    + (x.PickupStopId.HasValue ? 1 : 0);
                return count == 1;
            })
            .WithName("pickup")
            .WithMessage("Exactly one of pickupStationId or pickupStopId must be provided.");

        // Dropoff: at most one of StationId/StopId
        RuleFor(x => x)
            .Must(x =>
            {
                var count = (x.DropoffStationId.HasValue ? 1 : 0)
                    + (x.DropoffStopId.HasValue ? 1 : 0);
                return count <= 1;
            })
            .WithName("dropoff")
            .WithMessage("At most one of dropoffStationId or dropoffStopId may be provided.");

        // Seats — must have 1..5 entries
        RuleFor(x => x.Seats)
            .NotNull()
            .NotEmpty()
            .WithMessage("At least one seat is required.");

        RuleFor(x => x.Seats)
            .Must(s => s.Count <= 5)
            .When(x => x.Seats is { Count: > 0 })
            .WithErrorCode("BOOKING_MAX_SEATS_EXCEEDED")
            .WithMessage("A booking cannot exceed 5 seats.");

        RuleFor(x => x.Seats)
            .Must(HaveDistinctSeatNumbers)
            .When(x => x.Seats is { Count: > 0 })
            .WithName("seats")
            .WithMessage("Seat numbers must be unique.");

        RuleForEach(x => x.Seats).ChildRules(seat =>
        {
            seat.RuleFor(s => s.SeatNumber)
                .NotEmpty()
                .MaximumLength(20);
        });

        RuleFor(x => x.PaymentMethod)
            .NotEmpty()
            .Must(m => ValidPaymentMethods.Contains(m, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"paymentMethod must be one of: {string.Join(", ", ValidPaymentMethods)}.");

        When(x => x.ShuttlePickup is not null, () =>
        {
            RuleFor(x => x.ShuttlePickup!.Address).NotEmpty().MaximumLength(500);
            RuleFor(x => x.ShuttlePickup!.Latitude).InclusiveBetween(-90m, 90m);
            RuleFor(x => x.ShuttlePickup!.Longitude).InclusiveBetween(-180m, 180m);
        });

        When(x => x.ShuttleDropoff is not null, () =>
        {
            RuleFor(x => x.ShuttleDropoff!.Address).NotEmpty().MaximumLength(500);
            RuleFor(x => x.ShuttleDropoff!.Latitude).InclusiveBetween(-90m, 90m);
            RuleFor(x => x.ShuttleDropoff!.Longitude).InclusiveBetween(-180m, 180m);
        });
    }

    private static bool HaveDistinctSeatNumbers(IReadOnlyList<SeatRequest> seats)
        => seats.Select(seat => seat.SeatNumber.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == seats.Count;
}
