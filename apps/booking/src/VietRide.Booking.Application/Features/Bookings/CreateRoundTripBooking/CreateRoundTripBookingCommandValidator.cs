using FluentValidation;

namespace VietRide.Booking.Application.Features.Bookings.CreateRoundTripBooking;

/// <summary>
/// Input-shape and field validation for <see cref="CreateRoundTripBookingCommand"/>.
/// Business checks (trip status, return route, return window) live in the handler.
/// </summary>
public sealed class CreateRoundTripBookingCommandValidator : AbstractValidator<CreateRoundTripBookingCommand>
{
    private static readonly string[] ValidPaymentMethods = ["WALLET", "VNPAY"];

    public CreateRoundTripBookingCommandValidator()
    {
        RuleFor(x => x.PassengerUserId)
            .NotEmpty();

        RuleFor(x => x.Outbound)
            .NotNull()
            .SetValidator(new RoundTripBookingLegCommandValidator());

        RuleFor(x => x.Return)
            .NotNull()
            .SetValidator(new RoundTripBookingLegCommandValidator());

        RuleFor(x => x.PaymentMethod)
            .NotEmpty()
            .Must(m => ValidPaymentMethods.Contains(m, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"paymentMethod must be one of: {string.Join(", ", ValidPaymentMethods)}.");
    }

    public sealed class RoundTripBookingLegCommandValidator : AbstractValidator<CreateRoundTripBookingCommand.RoundTripBookingLegCommand>
    {
        public RoundTripBookingLegCommandValidator()
        {
            RuleFor(x => x.TripId)
                .NotEmpty();

            RuleFor(x => x)
                .Must(x => CountProvided(x.PickupStationId, x.PickupStopId) == 1)
                .WithName("pickup")
                .WithMessage("Exactly one of pickupStationId or pickupStopId must be provided.");

            RuleFor(x => x)
                .Must(x => CountProvided(x.DropoffStationId, x.DropoffStopId) <= 1)
                .WithName("dropoff")
                .WithMessage("At most one of dropoffStationId or dropoffStopId may be provided.");

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

        private static bool HaveDistinctSeatNumbers(IReadOnlyList<CreateRoundTripBookingCommand.RoundTripSeatRequest> seats)
            => seats.Select(seat => seat.SeatNumber.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == seats.Count;

        private static int CountProvided(Guid? first, Guid? second)
            => (first.HasValue ? 1 : 0) + (second.HasValue ? 1 : 0);
    }
}
