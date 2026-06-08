using FluentValidation;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.Admin.CreateOperator;

public sealed class CreateOperatorCommandValidator : AbstractValidator<CreateOperatorCommand>
{
    public CreateOperatorCommandValidator()
    {
        RuleFor(x => x.CallerRole).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
        RuleFor(x => x).Custom((command, context) =>
        {
            foreach (var field in command.UnsupportedSubscriptionFields)
            {
                context.AddFailure(field, "Paid plans and explicit subscription fields are not supported by this endpoint yet.");
            }
        });
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
        RuleFor(x => x.AddressDistrict).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AddressProvince).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RepresentativeName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RepresentativePhone)
            .NotEmpty()
            .MaximumLength(20)
            .Must(BeValidPhone)
            .WithMessage("Phone number must be a Vietnamese number in +84xxxxxxxxx or 0xxxxxxxxx format.");
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
