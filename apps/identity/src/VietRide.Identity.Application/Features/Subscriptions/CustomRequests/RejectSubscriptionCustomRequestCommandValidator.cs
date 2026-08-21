using FluentValidation;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed class RejectSubscriptionCustomRequestCommandValidator
    : AbstractValidator<RejectSubscriptionCustomRequestCommand>
{
    public RejectSubscriptionCustomRequestCommandValidator()
    {
        RuleFor(command => command.CallerUserId).NotEmpty();
        RuleFor(command => command.RequestId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(1000);
    }
}
