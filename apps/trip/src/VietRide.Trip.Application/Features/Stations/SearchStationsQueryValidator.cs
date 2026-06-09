using FluentValidation;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class SearchStationsQueryValidator : AbstractValidator<SearchStationsQuery>
{
    public SearchStationsQueryValidator()
    {
        RuleFor(query => query.Q)
            .NotEmpty()
            .WithMessage("Search query is required.");
    }
}
