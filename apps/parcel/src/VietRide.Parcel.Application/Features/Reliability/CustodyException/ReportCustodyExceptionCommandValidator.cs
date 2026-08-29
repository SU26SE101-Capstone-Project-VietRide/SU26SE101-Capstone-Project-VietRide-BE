using FluentValidation;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyException;

public sealed class ReportCustodyExceptionCommandValidator
    : AbstractValidator<ReportCustodyExceptionCommand>
{
    public ReportCustodyExceptionCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.ActorRole).NotEmpty().MaximumLength(32);
        RuleFor(x => x.ActorRole)
            .Must(role => string.Equals(role, "ASSISTANT", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only an Assistant can submit a custody exception report.");
        RuleFor(x => x.IncidentType)
            .Must(value => Enum.TryParse<ParcelIncidentType>(value, true, out _))
            .WithMessage("IncidentType is invalid.");
        RuleFor(x => x.ActualLocationType)
            .Must(value => Enum.TryParse<ParcelCustodyLocationType>(value, true, out _))
            .WithMessage("ActualLocationType is invalid.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.TemporaryExceptionTag).MaximumLength(100);
        RuleFor(x => x.ObservedWeightKg).GreaterThan(0).When(x => x.ObservedWeightKg.HasValue);
    }
}
