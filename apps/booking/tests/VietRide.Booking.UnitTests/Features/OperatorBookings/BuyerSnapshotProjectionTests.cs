using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.OperatorBookings;

public sealed class BuyerSnapshotProjectionTests
{
    [Fact]
    public async Task List_LegacyRowsUseOneBoundedIdentityBatchAndKeepBuyerDistinctFromSeats()
    {
        var buyerId = Guid.NewGuid();
        var repository = Substitute.For<IBookingRepository>();
        repository.ListOperatorBookingsAsync(
                Arg.Any<OperatorBookingListCriteria>(),
                Arg.Any<CancellationToken>())
            .Returns(new OperatorBookingListPage(
                [CreateListItem(buyerId, buyer: null)],
                1));
        var identity = Substitute.For<IIdentityUserServiceClient>();
        identity.GetUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, BookingBuyerSnapshotProfile>
            {
                [buyerId] = new(
                    buyerId,
                    "Nguyễn Văn Buyer",
                    "0900000000",
                    "buyer@example.test",
                    "https://example.test/avatar.jpg",
                    false),
            });
        var handler = new ListOperatorBookingsQueryHandler(repository, identity);

        var result = await handler.Handle(
            new ListOperatorBookingsQuery(Guid.NewGuid(), null, null, null, null, null),
            CancellationToken.None);

        var buyer = Assert.Single(result.Items).Buyer;
        Assert.NotNull(buyer);
        Assert.Equal(buyerId, buyer.UserId);
        Assert.Equal("Nguyễn Văn Buyer", buyer.DisplayName);
        Assert.Equal("0900000000", buyer.Phone);
        await identity.Received(1).GetUsersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { buyerId })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_SnapshottedRowsDoNotCallIdentity()
    {
        var buyerId = Guid.NewGuid();
        var repository = Substitute.For<IBookingRepository>();
        repository.ListOperatorBookingsAsync(
                Arg.Any<OperatorBookingListCriteria>(),
                Arg.Any<CancellationToken>())
            .Returns(new OperatorBookingListPage(
                [CreateListItem(
                    buyerId,
                    new OperatorBookingBuyerDto(buyerId, "Snapshot Buyer", null, null, null))],
                1));
        var identity = Substitute.For<IIdentityUserServiceClient>();
        var handler = new ListOperatorBookingsQueryHandler(repository, identity);

        var result = await handler.Handle(
            new ListOperatorBookingsQuery(Guid.NewGuid(), null, null, null, null, null),
            CancellationToken.None);

        Assert.Equal("Snapshot Buyer", Assert.Single(result.Items).Buyer!.DisplayName);
        await identity.DidNotReceive().GetUsersAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detail_SoftDeletedBuyerFallbackIsRedacted()
    {
        var buyerId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var repository = Substitute.For<IBookingRepository>();
        repository.GetOperatorBookingDetailAsync(bookingId, operatorId, Arg.Any<CancellationToken>())
            .Returns(CreateDetail(bookingId, buyerId));
        var identity = Substitute.For<IIdentityUserServiceClient>();
        identity.GetUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, BookingBuyerSnapshotProfile>
            {
                [buyerId] = new(buyerId, "Người dùng đã xóa", null, null, null, true),
            });
        var handler = new GetOperatorBookingDetailQueryHandler(repository, identity);

        var result = await handler.Handle(
            new GetOperatorBookingDetailQuery(bookingId, operatorId),
            CancellationToken.None);

        Assert.NotNull(result.Buyer);
        Assert.Equal("Người dùng đã xóa", result.Buyer.DisplayName);
        Assert.Null(result.Buyer.Phone);
        Assert.Null(result.Buyer.Email);
        Assert.Null(result.Buyer.AvatarUrl);
    }

    [Fact]
    public void NewBooking_CapturesBuyerSnapshotWithoutUsingFirstPassenger()
    {
        var buyerId = Guid.NewGuid();

        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Restore("VR-20260729-BUYER01"),
            buyerId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000),
            buyerDisplayName: "Buyer Account",
            buyerPhone: "0900000000",
            buyerEmail: "buyer@example.test",
            buyerAvatarUrl: "https://example.test/avatar.jpg");
        booking.AddPassenger("A01");

        Assert.Equal(buyerId, booking.PassengerUserId);
        Assert.Equal("Buyer Account", booking.BuyerDisplayName);
        Assert.Equal("0900000000", booking.BuyerPhone);
        Assert.Equal("buyer@example.test", booking.BuyerEmail);
        Assert.Equal("https://example.test/avatar.jpg", booking.BuyerAvatarUrl);
    }

    private static OperatorBookingListItem CreateListItem(
        Guid buyerId,
        OperatorBookingBuyerDto? buyer)
        => new(
            Guid.NewGuid(),
            "VR-20260729-LIST01",
            Guid.NewGuid(),
            "CONFIRMED",
            new OperatorBookingTripDto("Route", "Origin", "Destination", null, null),
            2,
            200_000,
            DateTimeOffset.Parse("2026-07-29T01:00:00Z"),
            buyer,
            buyerId);

    private static OperatorBookingDetailDto CreateDetail(Guid bookingId, Guid buyerId)
        => new(
            bookingId,
            "VR-20260729-DETAIL01",
            buyerId,
            Guid.NewGuid(),
            "CONFIRMED",
            new OperatorBookingTripDto("Route", "Origin", "Destination", null, null),
            1,
            100_000,
            0,
            100_000,
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.Parse("2026-07-29T01:00:00Z"),
            [],
            [],
            null);
}
