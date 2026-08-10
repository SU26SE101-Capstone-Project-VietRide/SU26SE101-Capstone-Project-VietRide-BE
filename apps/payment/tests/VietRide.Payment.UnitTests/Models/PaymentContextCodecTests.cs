using FluentAssertions;
using VietRide.Payment.Application.Models;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.UnitTests.Models;

public sealed class PaymentContextCodecTests
{
    [Fact]
    public void DeserializeTrusted_WhenLegacySubscriptionBuyerContainsAddressDistrict_RemainsCompatible()
    {
        var subscriptionId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var json = $$"""
            {
              "version": 1,
              "operatorSubscriptionId": "{{subscriptionId}}",
              "planId": "{{planId}}",
              "planName": "Business",
              "billingPeriod": "MONTHLY",
              "periodFrom": "2026-08-10T00:00:00Z",
              "periodTo": "2026-09-10T00:00:00Z",
              "buyerSnapshot": {
                "name": "VietRide Bus",
                "businessRegistrationNumber": "BRN-001",
                "taxCode": "0312345678",
                "contactEmail": "billing@vietride.test",
                "contactPhone": "+84901234567",
                "addressStreet": "1 Nguyen Hue",
                "addressWard": "Ben Nghe",
                "addressDistrict": "District 1",
                "addressProvince": "Ho Chi Minh City"
              }
            }
            """;

        var context = SubscriptionPaymentContextCodec.DeserializeTrusted(json);

        context.OperatorSubscriptionId.Should().Be(subscriptionId);
        context.PlanId.Should().Be(planId);
        context.BuyerSnapshot.AddressProvince.Should().Be("Ho Chi Minh City");
    }

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
