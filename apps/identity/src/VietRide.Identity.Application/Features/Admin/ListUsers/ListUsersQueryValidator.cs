using FluentValidation;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Admin.ListUsers;

public sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    private static readonly HashSet<string> AllowedRoles = new(
        Enum.GetNames<UserRole>(),
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> AllowedStatuses = new(
        Enum.GetNames<UserStatus>(),
        StringComparer.OrdinalIgnoreCase);

    public ListUsersQueryValidator()
    {
        RuleFor(query => query.CallerRole).NotEmpty();
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).When(query => query.PageSize.HasValue);
        RuleFor(query => query.Role)
            .Must(role => string.IsNullOrWhiteSpace(role) || AllowedRoles.Contains(role))
            .WithMessage("Role is not supported.");
        RuleFor(query => query.Status)
            .Must(status => string.IsNullOrWhiteSpace(status) || AllowedStatuses.Contains(status))
            .WithMessage("Status is not supported.");
        RuleFor(query => query.SortDir)
            .Must(direction => string.IsNullOrWhiteSpace(direction)
                || direction.Equals("asc", StringComparison.OrdinalIgnoreCase)
                || direction.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("SortDir must be 'asc' or 'desc'.");
        RuleFor(query => query)
            .Must(query => !query.From.HasValue || !query.To.HasValue || query.From <= query.To)
            .WithMessage("from must be on or before to.");
    }
}
