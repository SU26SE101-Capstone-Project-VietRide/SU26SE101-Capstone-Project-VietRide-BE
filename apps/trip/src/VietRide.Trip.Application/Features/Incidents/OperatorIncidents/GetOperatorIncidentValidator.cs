using FluentValidation;

namespace VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

public sealed class GetOperatorIncidentValidator : AbstractValidator<GetOperatorIncidentQuery>
{
    public GetOperatorIncidentValidator()
    {
        RuleFor(query => query.OperatorId).NotEmpty();
        RuleFor(query => query.IncidentId).NotEmpty();
    }
}
