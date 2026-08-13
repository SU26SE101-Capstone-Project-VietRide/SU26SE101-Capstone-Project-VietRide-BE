using FluentValidation;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Locations;

public sealed class ListAdminLocationsValidator : AbstractValidator<ListAdminLocationsQuery>
{
    public ListAdminLocationsValidator()
    {
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).When(query => query.PageSize.HasValue);
        RuleFor(query => query.Search).MaximumLength(255);
        RuleFor(query => query.Type)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || Location.IsSupportedType(value.Trim().ToUpperInvariant()))
            .WithMessage("Type must be PROVINCE, MUNICIPALITY, WARD, COMMUNE, or SPECIAL_ZONE.");
        RuleFor(query => query.ParentCode).MaximumLength(50);
    }
}
