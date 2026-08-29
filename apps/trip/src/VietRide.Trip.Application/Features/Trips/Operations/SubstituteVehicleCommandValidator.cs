using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class SubstituteVehicleCommandValidator : AbstractValidator<SubstituteVehicleCommand>
{
    public SubstituteVehicleCommandValidator()
    {
        RuleFor(command => command.TripId).NotEmpty();
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command.ReplacementVehicleId).NotEmpty();
        RuleFor(command => command.IncidentId).NotNull().NotEmpty();
        RuleFor(command => command.EstimatedRecoveryDepartureAt)
            .Must(value => value.Offset == TimeSpan.Zero)
            .WithMessage("must be an absolute UTC timestamp");
        RuleFor(command => command.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("must not be blank")
            .Must(reason => reason is null || reason.Trim().Length <= 500)
            .WithMessage("must not exceed 500 characters")
            .OverridePropertyName("reason");
        RuleFor(command => command.ReplacementCrewSpecified).Equal(true)
            .WithMessage("replacementCrew is required.");
        RuleFor(command => command.ReplacementDriverId).NotNull().NotEmpty();
        RuleFor(command => command.ReplacementAssistantId).NotNull().NotEmpty();
    }
}
