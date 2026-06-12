using FluentValidation;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.OperatorUsers.ListOperatorUsers;

public sealed class ListOperatorUsersQueryValidator : AbstractValidator<ListOperatorUsersQuery>
{
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "createdAt",
        "email",
        "displayName",
        "role",
        "status",
    };

    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        UserRole.DRIVER.ToString(),
        UserRole.ASSISTANT.ToString(),
        UserRole.OPERATOR_STAFF.ToString(),
    };

    private static readonly HashSet<string> AllowedStatuses = new(
        Enum.GetNames<UserStatus>(),
        StringComparer.OrdinalIgnoreCase);

    public ListOperatorUsersQueryValidator()
    {
        RuleFor(x => x.CallerRole).NotEmpty();
        RuleFor(x => x.Page).GreaterThan(0).When(x => x.Page.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).When(x => x.PageSize.HasValue);
        RuleFor(x => x.Search).MaximumLength(255);
        RuleFor(x => x.SortBy)
            .Must(sortBy => string.IsNullOrWhiteSpace(sortBy) || AllowedSortFields.Contains(sortBy))
            .WithMessage("SortBy is not supported.")
            .WithErrorCode("INVALID_SORT_FIELD");
        RuleFor(x => x.SortDir)
            .Must(sortDir => string.IsNullOrWhiteSpace(sortDir) || sortDir.Equals("asc", StringComparison.OrdinalIgnoreCase) || sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("SortDir must be 'asc' or 'desc'.");
        RuleFor(x => x.Role)
            .Must(role => string.IsNullOrWhiteSpace(role) || AllowedRoles.Contains(role))
            .WithMessage("Role must be DRIVER, ASSISTANT, or OPERATOR_STAFF.");
        RuleFor(x => x.Status)
            .Must(status => string.IsNullOrWhiteSpace(status) || AllowedStatuses.Contains(status))
            .WithMessage("Status is not supported.");
    }
}
