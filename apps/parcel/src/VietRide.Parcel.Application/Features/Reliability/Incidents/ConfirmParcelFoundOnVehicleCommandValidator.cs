using FluentValidation;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class ConfirmParcelFoundOnVehicleCommandValidator
    : AbstractValidator<ConfirmParcelFoundOnVehicleCommand>
{
    public ConfirmParcelFoundOnVehicleCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.IncidentId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.AssistantUserId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.ParcelCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Note).MaximumLength(2000);
        RuleForEach(x => x.EvidenceReferences)
            .NotEmpty()
            .MaximumLength(2048);
    }
}
