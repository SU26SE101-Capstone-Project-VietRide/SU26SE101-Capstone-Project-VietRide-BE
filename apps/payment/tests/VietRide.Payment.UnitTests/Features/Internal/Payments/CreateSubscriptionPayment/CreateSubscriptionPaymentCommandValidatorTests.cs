using FluentAssertions;
using VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;
using VietRide.Payment.Application.Models;

namespace VietRide.Payment.UnitTests.Features.Internal.Payments.CreateSubscriptionPayment;

public sealed class CreateSubscriptionPaymentCommandValidatorTests
{
    private const long ProratedAmount = 77_205_356;

    [Theory]
    [InlineData("VNPAY")]
    [InlineData("WALLET")]
    public void Validate_ProratedAmountToTheDong_IsValid(string paymentMethod)
    {
        var validator = new CreateSubscriptionPaymentCommandValidator();

        var result = validator.Validate(CreateCommand(paymentMethod, ProratedAmount));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveAmount_IsInvalid(long amount)
    {
        var validator = new CreateSubscriptionPaymentCommandValidator();
        var command = CreateCommand("VNPAY", amount);

        var result = validator.Validate(command);

        result.Errors.Should().Contain(error => error.PropertyName == nameof(command.Amount));
    }

    private static CreateSubscriptionPaymentCommand CreateCommand(string paymentMethod, long amount)
    {
        var subscriptionId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var periodFrom = new DateTimeOffset(2026, 9, 3, 5, 0, 0, TimeSpan.Zero);
        return new CreateSubscriptionPaymentCommand(
            Guid.NewGuid(),
            subscriptionId,
            Guid.NewGuid(),
            planId,
            "MONTHLY",
            paymentMethod,
            amount,
            new SubscriptionPaymentContextV1(
                1,
                subscriptionId,
                planId,
                "Professional",
                "MONTHLY",
                periodFrom,
                periodFrom.AddMonths(1),
                new SubscriptionBuyerSnapshotV1(
                    "VietRide Bus",
                    "BRN-001",
                    "0312345678",
                    "billing@vietride.test",
                    "+84901234567",
                    null,
                    null,
                    null)),
            Guid.NewGuid().ToString("D"),
            "203.0.113.10",
            periodFrom.AddMinutes(15),
            string.Equals(paymentMethod, "VNPAY", StringComparison.Ordinal) ? "OPERATOR_WEB" : null);
    }
}
