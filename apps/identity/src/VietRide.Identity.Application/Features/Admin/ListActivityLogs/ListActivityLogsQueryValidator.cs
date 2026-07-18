using FluentValidation;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Admin.ListActivityLogs;

public sealed class ListActivityLogsQueryValidator : AbstractValidator<ListActivityLogsQuery>
{
    private static readonly HashSet<string> AllowedActions = new(
        Enum.GetNames<ActivityLogAction>(),
        StringComparer.OrdinalIgnoreCase);

    public ListActivityLogsQueryValidator()
    {
        RuleFor(query => query.CallerRole).NotEmpty();
        RuleFor(query => query.Action)
            .Must(action => string.IsNullOrWhiteSpace(action) || AllowedActions.Contains(action))
            .WithMessage("Action is not supported.");
        RuleFor(query => query.From)
            .Must(value => !value.HasValue || value.Value.Offset == TimeSpan.Zero)
            .WithMessage("From must be an RFC 3339 UTC timestamp.");
        RuleFor(query => query.To)
            .Must(value => !value.HasValue || value.Value.Offset == TimeSpan.Zero)
            .WithMessage("To must be an RFC 3339 UTC timestamp.");
        RuleFor(query => query)
            .Must(query => !query.From.HasValue || !query.To.HasValue || query.From.Value < query.To.Value)
            .WithMessage("From must be earlier than To.");
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).When(query => query.PageSize.HasValue);
    }
}
