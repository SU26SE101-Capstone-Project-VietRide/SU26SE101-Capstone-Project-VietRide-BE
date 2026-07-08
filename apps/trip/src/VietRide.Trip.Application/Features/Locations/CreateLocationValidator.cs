using FluentValidation;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Locations;

public sealed class CreateLocationValidator : AbstractValidator<CreateLocationCommand>
{
    public CreateLocationValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty()
            .MaximumLength(20)
            .Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Code may contain only letters, numbers, underscores, and hyphens.");
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(100);
        RuleFor(command => command.Type)
            .NotEmpty()
            .Must(value => value is not null
                && (value.Equals(Location.ProvinceType, StringComparison.OrdinalIgnoreCase)
                    || value.Equals(Location.MunicipalityType, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Type must be PROVINCE or MUNICIPALITY.");
        RuleFor(command => command.SortOrder)
            .GreaterThanOrEqualTo(0)
            .When(command => command.SortOrder.HasValue);
    }
}
