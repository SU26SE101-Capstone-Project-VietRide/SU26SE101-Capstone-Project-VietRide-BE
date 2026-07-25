using FluentValidation;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed class GetVehicleSubstitutionImpactQueryValidator
    : AbstractValidator<GetVehicleSubstitutionImpactQuery>
{
    public GetVehicleSubstitutionImpactQueryValidator()
    {
        RuleFor(query => query.TripId)
            .Must(BeNonEmptyGuid)
            .WithMessage("tripId is required and must be a non-empty UUID.");
        RuleFor(query => query.OperatorId)
            .Must(BeNonEmptyGuid)
            .WithMessage("operatorId is required and must be a non-empty UUID.");
    }

    private static bool BeNonEmptyGuid(string? value)
        => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty;
}
