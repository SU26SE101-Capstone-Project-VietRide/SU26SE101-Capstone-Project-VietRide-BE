using FluentAssertions;
using VietRide.Payment.Application.Models;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.UnitTests.Models;

public sealed class PaymentContextCodecTests
{
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
