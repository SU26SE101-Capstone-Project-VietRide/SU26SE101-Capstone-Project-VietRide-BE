using FluentValidation;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Internal.Operators.IncrementOperatorUsage;

public sealed class IncrementOperatorUsageCommandValidator : AbstractValidator<IncrementOperatorUsageCommand>
{
    public IncrementOperatorUsageCommandValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.Resource)
            .NotEmpty()
            .Must(resource => Enum.TryParse<SubscriptionUsageResource>(resource, ignoreCase: false, out _));
        RuleFor(x => x.Delta).GreaterThan(0);
    }
}
