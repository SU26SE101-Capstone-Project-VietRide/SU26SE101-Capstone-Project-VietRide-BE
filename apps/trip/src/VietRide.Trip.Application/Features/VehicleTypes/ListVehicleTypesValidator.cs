using FluentValidation;

namespace VietRide.Trip.Application.Features.VehicleTypes;

public sealed class ListVehicleTypesValidator : AbstractValidator<ListVehicleTypesQuery>
{
    private static readonly HashSet<string> AllowedSearchFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "code",
        "displayName",
    };

    public ListVehicleTypesValidator()
    {
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).When(query => query.PageSize.HasValue);
        RuleFor(query => query.Search).MaximumLength(255);
        RuleFor(query => query.SearchIn)
            .Must(HaveOnlyAllowedSearchFields)
            .WithMessage("SearchIn fields must be 'code' and/or 'displayName'.");
        RuleFor(query => query.SortDir)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || value.Equals("asc", StringComparison.OrdinalIgnoreCase)
                || value.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("SortDir must be 'asc' or 'desc'.");
    }

    private static bool HaveOnlyAllowedSearchFields(string? searchIn)
    {
        if (string.IsNullOrWhiteSpace(searchIn))
            return true;

        var fields = searchIn.Split(',', StringSplitOptions.TrimEntries);
        return fields.Length > 0
            && fields.All(field => !string.IsNullOrWhiteSpace(field) && AllowedSearchFields.Contains(field));
    }
}
