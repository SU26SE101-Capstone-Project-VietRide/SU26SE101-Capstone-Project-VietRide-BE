using FluentValidation;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class ListAdminStationsValidator : AbstractValidator<ListAdminStationsQuery>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "name", "createdAt", "updatedAt" };

    public ListAdminStationsValidator()
    {
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).When(query => query.PageSize.HasValue);
        RuleFor(query => query.Search).MaximumLength(255);
        RuleFor(query => query.SortBy)
            .Must(value => string.IsNullOrWhiteSpace(value) || AllowedSortFields.Contains(value))
            .WithMessage("SortBy must be name, createdAt, or updatedAt.");
        RuleFor(query => query.SortDir)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || value.Equals("asc", StringComparison.OrdinalIgnoreCase)
                || value.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("SortDir must be 'asc' or 'desc'.");
    }
}
