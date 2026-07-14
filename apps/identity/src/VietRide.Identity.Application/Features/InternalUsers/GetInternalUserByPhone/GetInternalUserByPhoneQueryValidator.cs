using FluentValidation;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.InternalUsers.GetInternalUserByPhone;

public sealed class GetInternalUserByPhoneQueryValidator : AbstractValidator<GetInternalUserByPhoneQuery>
{
    public GetInternalUserByPhoneQueryValidator()
    {
        RuleFor(query => query.Phone)
            .NotEmpty()
            .Must(BeCanonicalPhone)
            .WithMessage("phone must be a canonical Vietnamese E.164 number.");
    }

    private static bool BeCanonicalPhone(string phone)
    {
        try
        {
            return PhoneNumber.Parse(phone).Value == phone;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
