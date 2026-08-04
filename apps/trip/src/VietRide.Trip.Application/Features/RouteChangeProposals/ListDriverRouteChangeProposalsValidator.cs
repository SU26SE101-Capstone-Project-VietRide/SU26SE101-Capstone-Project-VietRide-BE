using FluentValidation;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class ListDriverRouteChangeProposalsValidator : AbstractValidator<ListDriverRouteChangeProposalsQuery>
{
    public ListDriverRouteChangeProposalsValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Type)
            .Must(type => string.Equals(type, "EXISTING", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "CUSTOM", StringComparison.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.Type));
        RuleFor(x => x.Page).InclusiveBetween(1, 100).When(x => x.Page.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).When(x => x.PageSize.HasValue);
    }
}
