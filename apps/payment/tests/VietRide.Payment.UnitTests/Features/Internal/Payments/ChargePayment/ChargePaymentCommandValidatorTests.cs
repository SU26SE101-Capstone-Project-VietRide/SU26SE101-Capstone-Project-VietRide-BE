using FluentAssertions;
using VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;
using VietRide.Payment.Application.Models;

namespace VietRide.Payment.UnitTests.Features.Internal.Payments.ChargePayment;

public sealed class ChargePaymentCommandValidatorTests
{
    private readonly ChargePaymentCommandValidator _validator = new();

    [Fact]
    public void Validate_BookingGroupWithVnPay_IsValid()
    {
        var command = CreateCommand("BOOKING_GROUP", "VNPAY");

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_BookingGroupWithWallet_IsInvalid()
    {
        var command = CreateCommand("BOOKING_GROUP", "WALLET");

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error =>
            error.PropertyName == nameof(ChargePaymentCommand.ReferenceType));
    }

    private static ChargePaymentCommand CreateCommand(string referenceType, string method) =>
        new(
            referenceType,
            Guid.NewGuid(),
            Guid.NewGuid(),
            250_000,
            method,
            new PaymentContextV1(
                1,
                [
                    new PaymentAllocationV1(
                        Guid.NewGuid(),
                        "BOOKING",
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        125_000,
                        0,
                        0),
                    new PaymentAllocationV1(
                        Guid.NewGuid(),
                        "BOOKING",
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        125_000,
                        0,
                        0)
                ]),
            Guid.NewGuid().ToString("N"),
            "127.0.0.1");
}
