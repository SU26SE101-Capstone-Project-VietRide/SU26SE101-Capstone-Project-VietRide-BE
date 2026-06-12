using FluentValidation;

namespace VietRide.Payment.Application.Features.TopUps.CreateTopUp;

public sealed class CreateTopUpCommandValidator : AbstractValidator<CreateTopUpCommand>
{
    private const long MinimumTopUpAmount = 10_000;
    private const string VnPayMethod = "VNPAY";

    public CreateTopUpCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(MinimumTopUpAmount)
            .WithErrorCode("WALLET_TOP_UP_AMOUNT_TOO_LOW")
            .WithMessage("Top-up amount must be at least 10,000 VND.");

        RuleFor(x => x.Method)
            .NotEmpty()
            .Must(method => string.Equals(method, VnPayMethod, StringComparison.OrdinalIgnoreCase))
            .WithMessage("method must be VNPAY.");
    }
}
