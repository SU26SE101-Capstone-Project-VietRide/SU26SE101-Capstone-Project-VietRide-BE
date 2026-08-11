using FluentValidation;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class SearchStationsQueryValidator : AbstractValidator<SearchStationsQuery>
{
    public SearchStationsQueryValidator()
    {
        RuleFor(query => query)
            .Must(HasSearchCriteria)
            .WithName("SearchCriteria")
            .WithMessage("Provide q, city, ward, or locationId.");

        RuleFor(query => query.LocationId)
            .NotEqual(Guid.Empty)
            .WithMessage("Location id must not be empty.")
            .When(query => query.LocationId.HasValue);

        RuleFor(query => query.LocationScopeCode)
            .Matches("^(?:[0-9]{2}|[0-9]{5})$")
            .WithMessage("Location scope code must contain exactly 2 or 5 digits.")
            .When(query => !string.IsNullOrWhiteSpace(query.LocationScopeCode));

        RuleFor(query => query)
            .Must(query => !query.LocationId.HasValue || string.IsNullOrWhiteSpace(query.LocationScopeCode))
            .WithName(nameof(SearchStationsQuery.LocationScopeCode))
            .WithMessage("locationId and locationScopeCode cannot be used together.");
    }

    private static bool HasSearchCriteria(SearchStationsQuery query)
        => !string.IsNullOrWhiteSpace(query.Q)
            || !string.IsNullOrWhiteSpace(query.City)
            || !string.IsNullOrWhiteSpace(query.Ward)
            || query.LocationId.HasValue
            || !string.IsNullOrWhiteSpace(query.LocationScopeCode);
}
