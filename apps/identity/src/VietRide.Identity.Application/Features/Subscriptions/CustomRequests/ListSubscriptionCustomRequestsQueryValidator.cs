using FluentValidation;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed class ListSubscriptionCustomRequestsQueryValidator
    : AbstractValidator<ListSubscriptionCustomRequestsQuery>
{
    public ListSubscriptionCustomRequestsQueryValidator()
    {
        RuleFor(query => query.Status)
            .Must(status => string.IsNullOrWhiteSpace(status)
                || Enum.TryParse<SubscriptionCustomRequestStatus>(status, ignoreCase: false, out _))
            .WithMessage("Status must be PENDING_REVIEW, APPROVED, or REJECTED.");
    }
}
