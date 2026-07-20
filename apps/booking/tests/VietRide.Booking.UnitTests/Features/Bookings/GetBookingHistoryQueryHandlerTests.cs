using FluentAssertions;
using FluentValidation.TestHelper;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.History;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class GetBookingHistoryQueryHandlerTests
{
    [Fact]
    public async Task Handle_MapsBookingAndNestedTicketsAndForwardsOwnerFilters()
    {
        var userId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 7, 1, 2, 0, 0, TimeSpan.Zero);
        var departure = new DateTimeOffset(2026, 7, 2, 1, 0, 0, TimeSpan.Zero);
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Parse("VR-20260701-ABCDEFGH"),
            userId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            Money.FromRaw(350_000),
            Money.Zero,
            Money.FromRaw(350_000),
            "Origin",
            "Destination",
            departure,
            "Route name");
        booking.CreatedAt = createdAt;
        booking.AddTicketedPassenger(
            "A01",
            TicketCode.Parse("VT-20260701-ABCDEFGH"),
            Money.FromRaw(350_000),
            Money.Zero,
            Money.FromRaw(350_000));
        booking.Confirm(createdAt);
        var repository = Substitute.For<IBookingRepository>();
        repository.ListPassengerHistoryAsync(
                userId,
                BookingStatus.CONFIRMED,
                createdAt,
                createdAt.AddDays(2),
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<BookingEntity>.Create([booking], 1, 20, 1));
        var handler = new GetBookingHistoryQueryHandler(repository);

        var result = await handler.Handle(
            new GetBookingHistoryQuery(
                userId,
                "CONFIRMED",
                "2026-07-01T02:00:00Z",
                "2026-07-03T02:00:00Z",
                1,
                20),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        var item = result.Items[0];
        item.BookingCode.Should().Be("VR-20260701-ABCDEFGH");
        item.OriginName.Should().Be("Origin");
        item.DepartureDateTime.Should().Be(departure);
        item.Tickets.Should().ContainSingle();
        item.Tickets[0].Should().BeEquivalentTo(new BookingHistoryTicketDto(
            booking.Tickets[0].Id,
            "VT-20260701-ABCDEFGH",
            "A01",
            "ISSUED",
            350_000));
    }

    [Theory]
    [InlineData("1", null, null, 1, 20)]
    [InlineData("CONFIRMED", "2026-07-02T00:00:00Z", "2026-07-01T00:00:00Z", 1, 20)]
    [InlineData("CONFIRMED", null, null, 1, 101)]
    public void Validator_RejectsInvalidStatusRangeOrPageSize(
        string status,
        string? from,
        string? to,
        int page,
        int pageSize)
    {
        var validator = new GetBookingHistoryQueryValidator();

        var result = validator.TestValidate(
            new GetBookingHistoryQuery(Guid.NewGuid(), status, from, to, page, pageSize));

        result.IsValid.Should().BeFalse();
    }
}
