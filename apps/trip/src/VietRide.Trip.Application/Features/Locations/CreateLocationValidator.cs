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
            .Matches("^[0-9]+$")
            .WithMessage("Code must contain only digits from the official administrative catalog.");
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(100);
        RuleFor(command => command.Type)
            .NotEmpty()
            .Must(value => value is not null && Location.IsSupportedType(value.Trim().ToUpperInvariant()))
            .WithMessage("Type must be PROVINCE, MUNICIPALITY, WARD, COMMUNE, or SPECIAL_ZONE.");
        RuleFor(command => command.ParentCode)
            .MaximumLength(20)
            .Matches("^[0-9]+$")
            .When(command => !string.IsNullOrWhiteSpace(command.ParentCode));
        RuleFor(command => command.SortOrder)
            .GreaterThanOrEqualTo(0)
            .When(command => command.SortOrder.HasValue);
    }
}
