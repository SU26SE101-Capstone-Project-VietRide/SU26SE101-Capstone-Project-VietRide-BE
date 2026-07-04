using FluentValidation;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Update;

public sealed class UpdateParcelRouteFareCommandValidator : AbstractValidator<UpdateParcelRouteFareCommand>
{
    public UpdateParcelRouteFareCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();

        When(x => x.SizeCategory is not null, () =>
        {
            RuleFor(x => x.SizeCategory)
                .Must(BeValidSizeCategory)
                .WithMessage("'{PropertyValue}' is not a valid ParcelSizeCategory.");
        });

        When(x => x.PriceVnd.HasValue, () =>
        {
            RuleFor(x => x.PriceVnd!.Value)
                .GreaterThanOrEqualTo(1000);
        });

        RuleFor(x => x)
            .Must(x => x.PriceVnd.HasValue || x.EffectiveFrom.HasValue || x.EffectiveUntil is not null)
            .WithMessage("At least one updatable field must be supplied.");
    }

    private static bool BeValidSizeCategory(string value)
        => Enum.TryParse<ParcelSizeCategory>(value, ignoreCase: true, out _);
}
