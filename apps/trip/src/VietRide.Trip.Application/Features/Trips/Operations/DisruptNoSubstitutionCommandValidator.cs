using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class DisruptNoSubstitutionCommandValidator : AbstractValidator<DisruptNoSubstitutionCommand>
{
    public DisruptNoSubstitutionCommandValidator()
    {
        RuleFor(command => command.TripId)
            .NotEmpty()
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(command => command.OperatorId)
            .NotEmpty()
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(command => command.ActorUserId)
            .NotEmpty()
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(command => command.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("Disruption reason is required.")
            .WithErrorCode("VALIDATION_ERROR")
            .Must(reason => reason is null || reason.Trim().Length <= 500)
            .WithMessage("Disruption reason cannot exceed 500 characters.")
            .WithErrorCode("VALIDATION_ERROR");
    }
}
