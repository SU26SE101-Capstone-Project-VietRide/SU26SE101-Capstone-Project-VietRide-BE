using FluentValidation;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.Operators.RegisterOperator;

public sealed class RegisterOperatorCommandValidator : AbstractValidator<RegisterOperatorCommand>
{
    public RegisterOperatorCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.ContactPhone)
            .NotEmpty()
            .MaximumLength(20)
            .Must(BeValidPhone)
            .WithMessage("Phone number must be a Vietnamese number in +84xxxxxxxxx or 0xxxxxxxxx format.");
        RuleFor(x => x.BusinessRegistrationNumber).NotEmpty().MaximumLength(50);
        RuleFor(x => x.TaxCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.AddressStreet).NotEmpty().MaximumLength(255);
        RuleFor(x => x.AddressWard).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AddressProvince).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RepresentativeName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RepresentativePhone)
            .NotEmpty()
            .MaximumLength(20)
            .Must(BeValidPhone)
            .WithMessage("Phone number must be a Vietnamese number in +84xxxxxxxxx or 0xxxxxxxxx format.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
    }

    private static bool BeValidPhone(string phone)
    {
        try
        {
            PhoneNumber.Normalize(phone);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
