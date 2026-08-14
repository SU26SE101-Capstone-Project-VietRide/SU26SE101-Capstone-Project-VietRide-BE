using FluentValidation;

namespace VietRide.Identity.Application.Features.InternalUsers.SearchInternalUsers;

public sealed class SearchInternalUsersQueryValidator : AbstractValidator<SearchInternalUsersQuery>
{
    public SearchInternalUsersQueryValidator()
    {
        RuleFor(query => query.Search).NotEmpty().MaximumLength(100);
    }
}
