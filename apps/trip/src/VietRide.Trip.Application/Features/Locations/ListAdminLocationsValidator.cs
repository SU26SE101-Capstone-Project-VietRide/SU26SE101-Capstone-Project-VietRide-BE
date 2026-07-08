using FluentValidation;

namespace VietRide.Trip.Application.Features.Locations;

public sealed class ListAdminLocationsValidator : AbstractValidator<ListAdminLocationsQuery>
{
    public ListAdminLocationsValidator()
    {
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).When(query => query.PageSize.HasValue);
        RuleFor(query => query.Search).MaximumLength(255);
    }
}
