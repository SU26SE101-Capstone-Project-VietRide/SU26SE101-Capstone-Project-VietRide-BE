using FluentValidation;

namespace VietRide.Identity.Application.Features.InternalUsers.GetInternalUserByEmail;

public sealed class GetInternalUserByEmailQueryValidator : AbstractValidator<GetInternalUserByEmailQuery>
{
    public GetInternalUserByEmailQueryValidator()
    {
        RuleFor(query => query.Email)
            .NotEmpty()
            .MaximumLength(255)
            .EmailAddress();
    }
}
