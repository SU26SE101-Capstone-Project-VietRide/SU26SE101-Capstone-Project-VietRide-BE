using FluentAssertions;
using VietRide.Payment.Application.Models;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.UnitTests.Models;

public sealed class PaymentContextCodecTests
{
    [Fact]
    public void DeserializeTrusted_WhenLegacyAllocationHasNoReferenceCode_RemainsCompatible()
    {
        var json = $$"""
            {
              "version": 1,
              "allocations": [{
                "referenceId": "{{Guid.NewGuid()}}",
                "referenceType": "BOOKING",
                "operatorId": "{{Guid.NewGuid()}}",
                "tripId": "{{Guid.NewGuid()}}",
                "grossAmount": 100000,
                "voucherVietRideFundedAmount": 0,
                "voucherOperatorFundedAmount": 0
              }]
            }
            """;

        var context = PaymentContextCodec.DeserializeTrusted(json);

        context.Allocations.Should().ContainSingle()
            .Which.ReferenceCode.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSerialize_PreservesTrimmedReferenceCode()
    {
        var bookingId = Guid.NewGuid();
        var context = new PaymentContextV1(1,
        [
            new PaymentAllocationV1(
                bookingId,
                "BOOKING",
                Guid.NewGuid(),
                Guid.NewGuid(),
                100_000,
                0,
                0,
                "VR-20260810-ABCDEFGH"),
        ]);

        var json = PaymentContextCodec.ValidateAndSerialize(context, "BOOKING", bookingId, 100_000);

        PaymentContextCodec.DeserializeTrusted(json).Allocations[0].ReferenceCode
            .Should().Be("VR-20260810-ABCDEFGH");
    }

    [Theory]
    [InlineData(" untrimmed")]
    [InlineData("untrimmed ")]
    public void ValidateAndSerialize_WhenReferenceCodeIsNotCanonical_RejectsContext(string referenceCode)
    {
        var bookingId = Guid.NewGuid();
        var context = new PaymentContextV1(1,
        [
            new PaymentAllocationV1(
                bookingId,
                "BOOKING",
                Guid.NewGuid(),
                Guid.NewGuid(),
                100_000,
                0,
                0,
                referenceCode),
        ]);

        var action = () => PaymentContextCodec.ValidateAndSerialize(context, "BOOKING", bookingId, 100_000);

        action.Should().Throw<CodedValidationException>()
            .Where(exception => exception.ErrorCode == "PAYMENT_CONTEXT_INVALID");
    }

    [Fact]
    public void ValidateAndSerialize_WhenBookingGroupEconomicsMatch_RoundTripsCanonicalContext()
    {
        var firstBookingId = Guid.NewGuid();
        var secondBookingId = Guid.NewGuid();
        var context = new PaymentContextV1(1,
        [
            new PaymentAllocationV1(
                firstBookingId,
                "BOOKING",
                Guid.NewGuid(),
                Guid.NewGuid(),
                120_000,
                20_000,
                0),
            new PaymentAllocationV1(
                secondBookingId,
                "BOOKING",
                Guid.NewGuid(),
                Guid.NewGuid(),
                90_000,
                0,
                10_000),
        ]);

        var json = PaymentContextCodec.ValidateAndSerialize(
            context,
            "BOOKING_GROUP",
            Guid.NewGuid(),
            180_000);

        PaymentContextCodec.DeserializeTrusted(json).Should().BeEquivalentTo(context);
    }

    [Fact]
    public void ValidateAndSerialize_WhenPaidEconomicsMismatch_RejectsContext()
    {
        var bookingId = Guid.NewGuid();
        var context = new PaymentContextV1(1,
        [
            new PaymentAllocationV1(
                bookingId,
                "BOOKING",
                Guid.NewGuid(),
                Guid.NewGuid(),
                100_000,
                10_000,
                0),
        ]);

        var action = () => PaymentContextCodec.ValidateAndSerialize(
            context,
            "BOOKING",
            bookingId,
            100_000);

        action.Should().Throw<CodedValidationException>()
            .Where(exception => exception.ErrorCode == "PAYMENT_CONTEXT_INVALID");
    }
}
