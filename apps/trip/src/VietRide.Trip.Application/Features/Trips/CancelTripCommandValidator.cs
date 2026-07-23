using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips;

public sealed class CancelTripCommandValidator : AbstractValidator<CancelTripCommand>
{
    public CancelTripCommandValidator()
    {
        RuleFor(command => command.TripId).NotEmpty().WithErrorCode("VALIDATION_ERROR");
        RuleFor(command => command.OperatorId).NotEmpty().WithErrorCode("VALIDATION_ERROR");
        RuleFor(command => command.ActorUserId).NotEmpty().WithErrorCode("VALIDATION_ERROR");
        RuleFor(command => command.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("Cancellation reason is required.")
            .WithErrorCode("VALIDATION_ERROR")
            .MaximumLength(500)
            .WithErrorCode("VALIDATION_ERROR");
    }
}
