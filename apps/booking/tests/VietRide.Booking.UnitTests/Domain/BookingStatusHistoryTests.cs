using FluentAssertions;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.UnitTests.Domain;

public sealed class BookingStatusHistoryTests
{
    [Fact]
    public void Create_PreservesCanonicalLifecycleMetadata()
    {
        var bookingId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 7, 11, 2, 3, 4, TimeSpan.Zero);

        var history = BookingStatusHistory.Create(
            bookingId,
            BookingStatus.CANCELLED,
            occurredAt,
            BookingStatusHistorySource.CancelBooking,
            actorId,
            BookingCancellationReason.USER_INITIATED.ToString());

        history.Id.Should().NotBeEmpty();
        history.BookingId.Should().Be(bookingId);
        history.Status.Should().Be(BookingStatus.CANCELLED);
        history.OccurredAt.Should().Be(occurredAt);
        history.ActorUserId.Should().Be(actorId);
        history.ReasonCode.Should().Be("USER_INITIATED");
        history.Source.Should().Be("CANCEL_BOOKING");
    }

    [Fact]
    public void FrozenSources_AreExactlyTheReviewedSeven()
        => typeof(BookingStatusHistorySource).GetFields()
            .Select(field => field.GetRawConstantValue())
            .Should().BeEquivalentTo(new object?[]
            {
                "CREATE_BOOKING",
                "CREATE_ROUND_TRIP_BOOKING",
                "CONFIRM_ON_PAYMENT",
                "EXPIRE_ON_PAYMENT",
                "CANCEL_BOOKING",
                "MARK_REFUNDED",
                "COMPLETE_ON_TRIP_COMPLETED",
            });

    [Fact]
    public void Create_RejectsUnknownSourceAndUnboundedReason()
    {
        var actSource = () => BookingStatusHistory.Create(
            Guid.NewGuid(), BookingStatus.CONFIRMED, DateTimeOffset.UtcNow, "FUTURE_UNREVIEWED_WRITER");
        var actReason = () => BookingStatusHistory.Create(
            Guid.NewGuid(), BookingStatus.CANCELLED, DateTimeOffset.UtcNow,
            BookingStatusHistorySource.CancelBooking, reasonCode: new string('R', 101));

        actSource.Should().Throw<ArgumentException>();
        actReason.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("create_booking")]
    [InlineData("CREATE_BOOKING ")]
    [InlineData("PAYMENT_WEBHOOK")]
    public void Create_RejectsEveryNonCanonicalSource(string source)
    {
        var act = () => BookingStatusHistory.Create(
            Guid.NewGuid(), BookingStatus.PENDING_PAYMENT, DateTimeOffset.UtcNow, source);

        act.Should().Throw<ArgumentException>().WithParameterName("source");
    }
}
