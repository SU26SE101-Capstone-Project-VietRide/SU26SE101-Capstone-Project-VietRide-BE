using FluentValidation;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed class GetTripNotificationRecipientsQueryValidator
    : AbstractValidator<GetTripNotificationRecipientsQuery>
{
    public GetTripNotificationRecipientsQueryValidator()
    {
        RuleFor(query => query.TripId)
            .Must(BeNonEmptyGuid)
            .WithMessage("tripId is required and must be a non-empty UUID.");
    }

    private static bool BeNonEmptyGuid(string? value)
        => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty;
}
