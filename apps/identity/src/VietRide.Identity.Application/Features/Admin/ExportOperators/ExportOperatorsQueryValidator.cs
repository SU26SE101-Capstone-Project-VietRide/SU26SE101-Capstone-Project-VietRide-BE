using FluentValidation;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Admin.ExportOperators;

public sealed class ExportOperatorsQueryValidator : AbstractValidator<ExportOperatorsQuery>
{
    public ExportOperatorsQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || Enum.TryParse<OperatorRegistrationStatus>(value, true, out _));
        RuleFor(x => x.SortDir)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || value.Equals("asc", StringComparison.OrdinalIgnoreCase)
                || value.Equals("desc", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.DateField)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || value.Equals("createdAt", StringComparison.OrdinalIgnoreCase)
                || value.Equals("approvedAt", StringComparison.OrdinalIgnoreCase));
    }
}
