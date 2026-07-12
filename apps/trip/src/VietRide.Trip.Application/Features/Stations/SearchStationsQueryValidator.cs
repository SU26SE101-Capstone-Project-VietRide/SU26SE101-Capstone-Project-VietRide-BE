using FluentValidation;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class SearchStationsQueryValidator : AbstractValidator<SearchStationsQuery>
{
    public SearchStationsQueryValidator()
    {
        RuleFor(query => query)
            .Must(HasSearchCriteria)
            .WithName("SearchCriteria")
            .WithMessage("Provide q, city, province, or locationId.");

        RuleFor(query => query.LocationId)
            .NotEqual(Guid.Empty)
            .WithMessage("Location id must not be empty.")
            .When(query => query.LocationId.HasValue);
    }

    private static bool HasSearchCriteria(SearchStationsQuery query)
        => !string.IsNullOrWhiteSpace(query.Q)
            || !string.IsNullOrWhiteSpace(query.City)
            || !string.IsNullOrWhiteSpace(query.Province)
            || query.LocationId.HasValue;
}
