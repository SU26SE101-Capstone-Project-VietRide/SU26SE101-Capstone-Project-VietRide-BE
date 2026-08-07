using FluentValidation;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class GetOperatorShuttleTripsQueryValidator : AbstractValidator<GetOperatorShuttleTripsQuery>
{
    private static readonly HashSet<string> AllowedStatuses =
        [
            ShuttleTrip.ScheduledStatus,
            ShuttleTrip.InProgressStatus,
            ShuttleTrip.CompletedStatus,
            ShuttleTrip.CancelledStatus,
        ];

    public GetOperatorShuttleTripsQueryValidator()
    {
        RuleFor(query => query.OperatorId).NotEmpty();
        RuleFor(query => query.Page).GreaterThan(0);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.From)
            .Must((query, from) => !from.HasValue || !query.To.HasValue || from.Value <= query.To.Value)
            .WithMessage("From must be earlier than or equal to To.");
        RuleFor(query => query.From)
            .Must(value => !value.HasValue || value.Value != DateOnly.MaxValue)
            .WithMessage("From is outside the supported date range.");
        RuleFor(query => query.To)
            .Must(value => !value.HasValue || value.Value != DateOnly.MaxValue)
            .WithMessage("To is outside the supported date range.");
        RuleForEach(query => query.Statuses)
            .Must(status => AllowedStatuses.Contains(status))
            .WithMessage("Status must be SCHEDULED, IN_PROGRESS, COMPLETED, or CANCELLED.");
    }
}
