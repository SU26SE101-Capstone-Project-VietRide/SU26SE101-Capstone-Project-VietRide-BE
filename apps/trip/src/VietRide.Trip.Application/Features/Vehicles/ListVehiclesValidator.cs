using FluentValidation;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed class ListVehiclesValidator : AbstractValidator<ListVehiclesQuery>
{
    private static readonly HashSet<string> AllowedSearchFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "licensePlate",
    };

    public ListVehiclesValidator()
    {
        RuleFor(query => query.OperatorId).NotEmpty();
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).When(query => query.PageSize.HasValue);
        RuleFor(query => query.Search).MaximumLength(255);
        RuleFor(query => query.SearchIn)
            .Must(value => string.IsNullOrWhiteSpace(value) || AllowedSearchFields.Contains(value))
            .WithMessage("SearchIn must be 'licensePlate'.");
        RuleFor(query => query.SortDir)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || value.Equals("asc", StringComparison.OrdinalIgnoreCase)
                || value.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("SortDir must be 'asc' or 'desc'.");
        RuleFor(query => query.VehicleTypeId)
            .NotEqual(Guid.Empty)
            .When(query => query.VehicleTypeId.HasValue);
        RuleFor(query => query.Status)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || Enum.GetNames<VehicleStatus>().Contains(
                    value.Trim(),
                    StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<VehicleStatus>())}.");
    }
}
