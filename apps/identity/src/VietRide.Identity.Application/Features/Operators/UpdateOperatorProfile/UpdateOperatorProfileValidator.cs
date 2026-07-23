using System.Text.Json;
using FluentValidation;
using VietRide.Shared.Application.Security;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.Operators;

public sealed class UpdateOperatorProfileValidator : AbstractValidator<UpdateOperatorProfileCommand>
{
    public UpdateOperatorProfileValidator(IFirebaseStorageImageUrlValidator firebaseUrls)
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.CallerRole).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(255);
        RuleFor(command => command.ContactPhone)
            .NotEmpty()
            .MaximumLength(20)
            .Must(BeValidPhone)
            .WithMessage("Phone number must be a Vietnamese number in +84xxxxxxxxx or 0xxxxxxxxx format.");
        RuleFor(command => command.LogoUrl)
            .MaximumLength(2048)
            .Must((command, logoUrl) => logoUrl is null || firebaseUrls.IsValidOwnedImageUrl(
                logoUrl,
                $"operators/{command.OperatorId:D}/logo/"))
            .WithMessage("LogoUrl must be an owned Firebase operator logo URL.");
        RuleFor(command => command.AddressStreet).NotEmpty().MaximumLength(255);
        RuleFor(command => command.AddressWard).NotEmpty().MaximumLength(100);
        RuleFor(command => command.AddressDistrict).NotEmpty().MaximumLength(100);
        RuleFor(command => command.AddressProvince).NotEmpty().MaximumLength(100);
        RuleFor(command => command.RepresentativeName).NotEmpty().MaximumLength(255);
        RuleFor(command => command.RepresentativePhone)
            .NotEmpty()
            .MaximumLength(20)
            .Must(BeValidPhone)
            .WithMessage("Phone number must be a Vietnamese number in +84xxxxxxxxx or 0xxxxxxxxx format.");

        RuleFor(command => command)
            .Custom((command, context) =>
            {
                ValidatePolicy(context, nameof(command.CancellationPolicy), () =>
                    OperatorProfilePolicyValidator.NormalizeCancellationPolicy(command.CancellationPolicy));
                ValidatePolicy(context, nameof(command.ParcelNoShowPolicy), () =>
                    OperatorProfilePolicyValidator.NormalizeParcelNoShowPolicy(command.ParcelNoShowPolicy));
                ValidatePolicy(context, nameof(command.LuggagePolicy), () =>
                    OperatorProfilePolicyValidator.NormalizeLuggagePolicy(command.LuggagePolicy));
            });
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

    private static void ValidatePolicy(ValidationContext<UpdateOperatorProfileCommand> context, string propertyName, Action validate)
    {
        try
        {
            validate();
        }
        catch (ValidationException exception)
        {
            context.AddFailure(propertyName, exception.Message);
        }
        catch (JsonException exception)
        {
            context.AddFailure(propertyName, exception.Message);
        }
    }
}
