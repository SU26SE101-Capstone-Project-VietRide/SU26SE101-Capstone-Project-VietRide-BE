using FluentValidation;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Batch;

public sealed class BatchParcelRouteFareCommandValidator : AbstractValidator<BatchParcelRouteFareCommand>
{
    private const int MinimumItems = 1;
    private const int MaximumItems = 4;

    public BatchParcelRouteFareCommandValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.RouteId).NotEmpty();
        RuleFor(command => command.EffectiveFrom).NotEmpty();
        RuleFor(command => command.Items)
            .NotNull()
            .Must(items => items.Count is >= MinimumItems and <= MaximumItems)
            .WithMessage($"Items must contain between {MinimumItems} and {MaximumItems} entries.")
            .Must(HaveUniqueSizeCategories)
            .WithMessage("Items must contain unique sizeCategory values.");

        RuleForEach(command => command.Items).ChildRules(item =>
        {
            item.RuleFor(value => value.SizeCategory)
                .NotEmpty()
                .Must(BeCurrentSizeCategory)
                .WithMessage("'{PropertyValue}' is not a valid ParcelSizeCategory.");
            item.RuleFor(value => value.PriceVnd)
                .GreaterThan(0)
                .WithMessage("PriceVnd must be a positive whole VND amount.");
        });

        RuleFor(command => command.EffectiveUntil)
            .Must((command, effectiveUntil) =>
                !effectiveUntil.HasValue || effectiveUntil.Value > command.EffectiveFrom)
            .WithMessage("EffectiveUntil must be after EffectiveFrom.");
    }

    private static bool HaveUniqueSizeCategories(IReadOnlyList<BatchParcelRouteFareItem> items)
    {
        var categories = items
            .Select(item => CanonicalSizeCategory(item.SizeCategory))
            .Where(category => category is not null)
            .ToArray();
        return categories.Distinct().Count() == categories.Length;
    }

    private static bool BeCurrentSizeCategory(string? value)
        => CanonicalSizeCategory(value) is not null;

    private static ParcelSizeCategory? CanonicalSizeCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (var category in Enum.GetValues<ParcelSizeCategory>())
        {
            if (string.Equals(category.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        return null;
    }
}
