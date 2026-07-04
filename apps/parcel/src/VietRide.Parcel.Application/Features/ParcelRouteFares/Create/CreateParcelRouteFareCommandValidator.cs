using FluentValidation;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Create;

public sealed class CreateParcelRouteFareCommandValidator : AbstractValidator<CreateParcelRouteFareCommand>
{
    public CreateParcelRouteFareCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.SizeCategory)
            .NotEmpty()
            .Must(BeValidSizeCategory)
            .WithMessage("'{PropertyValue}' is not a valid ParcelSizeCategory.");
        RuleFor(x => x.PriceVnd).GreaterThanOrEqualTo(1000);
        RuleFor(x => x.EffectiveFrom).NotEmpty();

        When(x => x.EffectiveUntil.HasValue, () =>
        {
            RuleFor(x => x.EffectiveUntil!.Value)
                .GreaterThan(x => x.EffectiveFrom)
                .WithMessage("EffectiveUntil must be after EffectiveFrom.");
        });
    }

    private static bool BeValidSizeCategory(string value)
        => Enum.TryParse<ParcelSizeCategory>(value, ignoreCase: true, out _);
}
