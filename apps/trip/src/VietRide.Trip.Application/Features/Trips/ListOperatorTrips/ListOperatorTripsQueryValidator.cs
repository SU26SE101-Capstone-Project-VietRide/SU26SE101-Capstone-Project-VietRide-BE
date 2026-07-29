using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips.ListOperatorTrips;

public sealed class ListOperatorTripsQueryValidator : AbstractValidator<ListOperatorTripsQuery>
{
    public ListOperatorTripsQueryValidator()
    {
        RuleFor(query => query.OperatorId).NotEmpty();
        RuleFor(query => query.Search).MaximumLength(255);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).When(query => query.PageSize.HasValue);
        RuleFor(query => query.From)
            .Must((query, from) => !from.HasValue || !query.To.HasValue || from.Value <= query.To.Value)
            .WithMessage("From must be earlier than or equal to To.");
        RuleFor(query => query.From)
            .Must(from => from != DateOnly.MaxValue)
            .WithMessage("From is outside the supported date range.");
        RuleFor(query => query.To)
            .Must(to => to != DateOnly.MaxValue)
            .WithMessage("To is outside the supported date range.");
        RuleFor(query => query.SortBy)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || value.Equals("departureAt", StringComparison.OrdinalIgnoreCase))
            .WithMessage("SortBy must be 'departureAt'.");
        RuleFor(query => query.SortDir)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || value.Equals("asc", StringComparison.OrdinalIgnoreCase)
                || value.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("SortDir must be 'asc' or 'desc'.");
    }
}
