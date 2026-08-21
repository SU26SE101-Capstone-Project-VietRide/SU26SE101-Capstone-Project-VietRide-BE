using FluentValidation;

namespace VietRide.Identity.Application.Features.Subscriptions.ConfirmSubscriptionUpgradePayment;

public sealed class ConfirmSubscriptionUpgradePaymentCommandValidator
    : AbstractValidator<ConfirmSubscriptionUpgradePaymentCommand>
{
    public ConfirmSubscriptionUpgradePaymentCommandValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.UpgradeAttemptId).NotEmpty();
        RuleFor(command => command.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(100)
            .Must(BeUuidV4)
            .WithMessage("Idempotency-Key must be a UUID v4 value.");
        RuleFor(command => command.ClientIpAddress).NotEmpty().MaximumLength(64);
    }

    private static bool BeUuidV4(string value)
        => Guid.TryParse(value, out var id) && (id.ToByteArray()[7] >> 4) == 4;
}
