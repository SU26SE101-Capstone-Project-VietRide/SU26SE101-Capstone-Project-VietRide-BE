using FluentValidation;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Admin.ListOperators;

public sealed class ListOperatorsQueryValidator : AbstractValidator<ListOperatorsQuery>
{
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "contactEmail",
        "contactPhone",
        "businessRegistrationNumber",
        "taxCode",
        "registrationStatus",
        "isActive",
        "createdAt",
        "approvedAt",
        "suspendedAt",
    };

    private static readonly HashSet<string> AllowedStatuses = new(
        Enum.GetNames<OperatorRegistrationStatus>(),
        StringComparer.OrdinalIgnoreCase);

    public ListOperatorsQueryValidator()
    {
        RuleFor(x => x.CallerRole).NotEmpty();
        RuleFor(x => x.Page).GreaterThan(0).When(x => x.Page.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).When(x => x.PageSize.HasValue);
        RuleFor(x => x.Search).MaximumLength(255);
        RuleFor(x => x.SortBy)
            .Must(sortBy => string.IsNullOrWhiteSpace(sortBy) || AllowedSortFields.Contains(sortBy))
            .WithMessage("SortBy is not supported.");
        RuleFor(x => x.SortDir)
            .Must(sortDir => string.IsNullOrWhiteSpace(sortDir) || sortDir.Equals("asc", StringComparison.OrdinalIgnoreCase) || sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("SortDir must be 'asc' or 'desc'.");
        RuleFor(x => x.Status)
            .Must(status => string.IsNullOrWhiteSpace(status) || AllowedStatuses.Contains(status))
            .WithMessage("Status must be PENDING, APPROVED, REJECTED, or SUSPENDED.");
    }
}
