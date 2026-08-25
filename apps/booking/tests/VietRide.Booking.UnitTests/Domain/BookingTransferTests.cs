using FluentAssertions;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.UnitTests.Domain;

public sealed class BookingTransferTests
{
    [Fact]
    public void CreatesExactConfirmationStateAndConfirmsIdempotentlyWithoutChangingHistory()
    {
        var transferredAt = new DateTimeOffset(2026, 7, 26, 2, 30, 0, TimeSpan.Zero);
        var confirmedAt = transferredAt.AddMinutes(5);
        var confirmer = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var repeatedConfirmer = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var transfer = CreateTransfer(
            BookingTransferConfirmationStatus.PENDING_CONFIRM,
            newSeatNumber: " B02 ",
            transferredAt);

        transfer.OriginalSeatNumber.Should().BeNull();
        transfer.NewSeatNumber.Should().Be("B02");
        transfer.ConfirmationStatus.Should().Be(BookingTransferConfirmationStatus.PENDING_CONFIRM);
        transfer.ConfirmedAt.Should().BeNull();
        transfer.ConfirmedByUserId.Should().BeNull();
        transfer.CreatedAt.Should().Be(transferredAt);

        transfer.Confirm(confirmer, confirmedAt);
        transfer.Confirm(repeatedConfirmer, confirmedAt.AddMinutes(1));

        transfer.ConfirmationStatus.Should().Be(BookingTransferConfirmationStatus.CONFIRMED);
        transfer.ConfirmedAt.Should().Be(confirmedAt);
        transfer.ConfirmedByUserId.Should().Be(confirmer);
        transfer.TransferredAt.Should().Be(transferredAt);
        transfer.OriginalSeatNumber.Should().BeNull();
        transfer.NewSeatNumber.Should().Be("B02");
    }

    [Fact]
    public void EscalatedTransferCanStillBeConfirmedAndKeepsSeatTypeEvidence()
    {
        var transferredAt = new DateTimeOffset(2026, 8, 25, 1, 0, 0, TimeSpan.Zero);
        var transfer = BookingTransfer.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "A01",
            "B01",
            BookingTransferConfirmationStatus.PENDING_CONFIRM,
            transferredAt,
            Guid.NewGuid(),
            originalSeatType: " VIP ",
            newSeatType: "standard",
            isSeatDowngrade: true);

        transfer.Escalate().Should().BeTrue();
        transfer.Escalate().Should().BeFalse();
        transfer.ConfirmationStatus.Should().Be(BookingTransferConfirmationStatus.ESCALATED);
        transfer.OriginalSeatType.Should().Be("VIP");
        transfer.NewSeatType.Should().Be("STANDARD");
        transfer.IsSeatDowngrade.Should().BeTrue();

        transfer.Confirm(Guid.NewGuid(), transferredAt.AddHours(3));

        transfer.ConfirmationStatus.Should().Be(BookingTransferConfirmationStatus.CONFIRMED);
    }

    [Theory]
    [InlineData(BookingTransferConfirmationStatus.PENDING_CONFIRM, null)]
    [InlineData(BookingTransferConfirmationStatus.NOT_REQUIRED, "B02")]
    public void ConfirmRejectsASeatlessOrNonPendingTransfer(
        BookingTransferConfirmationStatus status,
        string? newSeatNumber)
    {
        var transfer = CreateTransfer(
            status,
            newSeatNumber,
            new DateTimeOffset(2026, 7, 26, 2, 30, 0, TimeSpan.Zero));

        var action = () => transfer.Confirm(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            new DateTimeOffset(2026, 7, 26, 2, 35, 0, TimeSpan.Zero));

        action.Should().Throw<InvalidOperationException>();
        transfer.ConfirmationStatus.Should().Be(status);
        transfer.ConfirmedAt.Should().BeNull();
        transfer.ConfirmedByUserId.Should().BeNull();
    }

    private static BookingTransfer CreateTransfer(
        BookingTransferConfirmationStatus status,
        string? newSeatNumber,
        DateTimeOffset transferredAt)
        => BookingTransfer.Create(
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
            null,
            Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"),
            Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"),
            null,
            newSeatNumber,
            status,
            transferredAt,
            Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"),
            " transfer chain ");
}
