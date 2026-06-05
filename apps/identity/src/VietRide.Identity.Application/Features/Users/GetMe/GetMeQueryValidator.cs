using FluentValidation;

namespace VietRide.Identity.Application.Features.Users.GetMe;

/// <summary>Input-shape validation for <see cref="GetMeQuery"/>.</summary>
public sealed class GetMeQueryValidator : AbstractValidator<GetMeQuery>
{
    public GetMeQueryValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();
    }
}
