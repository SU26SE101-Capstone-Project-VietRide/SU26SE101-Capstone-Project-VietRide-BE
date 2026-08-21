using FluentValidation;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Reliability.ReportIncident;

public sealed class ReportParcelIncidentCommandValidator
    : AbstractValidator<ReportParcelIncidentCommand>
{
    public ReportParcelIncidentCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.ReporterUserId).NotEmpty();
        RuleFor(x => x.IncidentType)
            .Must(value => Enum.TryParse<ParcelIncidentType>(value, true, out _))
            .WithMessage("IncidentType is invalid.");
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}
