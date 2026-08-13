using FluentValidation;

namespace VietRide.Trip.Application.Features.Incidents.ResolveIncident;

public sealed class ResolveIncidentCommandValidator : AbstractValidator<ResolveIncidentCommand>
{
    public ResolveIncidentCommandValidator()
    {
        RuleFor(command => command.OperatorId)
            .NotEmpty()
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(command => command.ActorUserId)
            .NotEmpty()
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(command => command.IncidentId)
            .NotEmpty()
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(command => command.ResolutionNote)
            .NotEmpty()
            .MaximumLength(1000)
            .WithErrorCode("VALIDATION_ERROR");
    }
}
