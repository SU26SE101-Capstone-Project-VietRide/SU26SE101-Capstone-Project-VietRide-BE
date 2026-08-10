using FluentValidation;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

public sealed class ListOperatorIncidentsValidator : AbstractValidator<ListOperatorIncidentsQuery>
{
    private static readonly IReadOnlySet<string> Categories =
        Enum.GetNames<IncidentCategory>().ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> Statuses =
        Enum.GetNames<OperatorIncidentStatusFilter>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    public ListOperatorIncidentsValidator()
    {
        RuleFor(query => query.OperatorId).NotEmpty();
        RuleFor(query => query.TripId).NotEqual(Guid.Empty).When(query => query.TripId.HasValue);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).When(query => query.PageSize.HasValue);
        RuleFor(query => query.Category)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || Categories.Contains(value.Trim()))
            .WithMessage("Category must be TRAFFIC_JAM, VEHICLE_BREAKDOWN, ACCIDENT, WEATHER, or OTHER.");
        RuleFor(query => query.Status)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || Statuses.Contains(value.Trim()))
            .WithMessage("Status must be OPEN or RESOLVED.");
        RuleFor(query => query)
            .Must(query => !query.From.HasValue || !query.To.HasValue || query.To.Value >= query.From.Value)
            .WithMessage("To must be on or after From.");
        RuleFor(query => query.To)
            .Must(to => to != DateOnly.MaxValue)
            .WithMessage("To is outside the supported date range.");
    }
}
