using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using VietRide.Booking.Api.Controllers;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Bookings.History;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Cqrs;
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
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        var handler = new GetBookingHistoryQueryHandler(repository, paymentLookup);

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
        item.PaymentRedirectUrl.Should().BeNull();
        await paymentLookup.DidNotReceiveWithAnyArgs()
            .LookupAsync(default, default!, default);
    }

    [Fact]
    public async Task Handle_EnrichesOneWayAndRoundTripUsingExactAuthoritativeTotalsWithOneDeduplicatedCall()
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var oneWay = CreatePendingBooking(userId, 350_000);
        var outbound = CreatePendingBooking(userId, 200_000, groupId, TripDirection.OUTBOUND);
        var inbound = CreatePendingBooking(userId, 300_000, groupId, TripDirection.RETURN);
        var repository = Substitute.For<IBookingRepository>();
        repository.ListPassengerHistoryAsync(
                userId,
                null,
                null,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<BookingEntity>.Create([oneWay, outbound, inbound], 1, 20, 3));
        repository.GetBookingGroupNetTotalsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(groupId)),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, long> { [groupId] = 500_000 });
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        paymentLookup.LookupAsync(
                userId,
                Arg.Any<IReadOnlyCollection<PaymentRedirectLookupReference>>(),
                Arg.Any<CancellationToken>())
            .Returns([
                LookupItem("BOOKING", oneWay.Id, 350_000, "https://sandbox.vnpayment.vn/one-way"),
                LookupItem("BOOKING_GROUP", groupId, 500_000, "https://sandbox.vnpayment.vn/round-trip"),
            ]);
        var handler = new GetBookingHistoryQueryHandler(repository, paymentLookup);

        var result = await handler.Handle(
            new GetBookingHistoryQuery(userId, null, null, null, 1, 20),
            CancellationToken.None);

        result.Items.Single(item => item.BookingId == oneWay.Id).PaymentRedirectUrl
            .Should().Be("https://sandbox.vnpayment.vn/one-way");
        result.Items.Where(item => item.BookingGroupId == groupId)
            .Should().OnlyContain(item => item.PaymentRedirectUrl == "https://sandbox.vnpayment.vn/round-trip");
        await paymentLookup.Received(1).LookupAsync(
            userId,
            Arg.Is<IReadOnlyCollection<PaymentRedirectLookupReference>>(references =>
                references.Count == 2
                && references.Count(reference => reference.ReferenceType == "BOOKING"
                    && reference.ReferenceId == oneWay.Id) == 1
                && references.Count(reference => reference.ReferenceType == "BOOKING_GROUP"
                    && reference.ReferenceId == groupId) == 1),
            Arg.Any<CancellationToken>());
        await repository.Received(1).GetBookingGroupNetTotalsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { groupId })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RejectsAmountMismatchDuplicateAndMissingLookupItems()
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var oneWay = CreatePendingBooking(userId, 350_000);
        var groupBooking = CreatePendingBooking(userId, 200_000, groupId, TripDirection.OUTBOUND);
        var missing = CreatePendingBooking(userId, 125_000);
        var repository = Substitute.For<IBookingRepository>();
        repository.ListPassengerHistoryAsync(userId, null, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<BookingEntity>.Create([oneWay, groupBooking, missing], 1, 20, 3));
        repository.GetBookingGroupNetTotalsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, long> { [groupId] = 500_000 });
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        paymentLookup.LookupAsync(userId, Arg.Any<IReadOnlyCollection<PaymentRedirectLookupReference>>(), Arg.Any<CancellationToken>())
            .Returns([
                LookupItem("BOOKING", oneWay.Id, 349_999, "https://sandbox.vnpayment.vn/wrong-amount"),
                LookupItem("BOOKING_GROUP", groupId, 500_000, "https://sandbox.vnpayment.vn/duplicate-1"),
                LookupItem("BOOKING_GROUP", groupId, 500_000, "https://sandbox.vnpayment.vn/duplicate-2"),
            ]);
        var handler = new GetBookingHistoryQueryHandler(repository, paymentLookup);

        var result = await handler.Handle(
            new GetBookingHistoryQuery(userId, null, null, null, 1, 20),
            CancellationToken.None);

        result.Items.Should().OnlyContain(item => item.PaymentRedirectUrl == null);
    }

    [Fact]
    public async Task Handle_WhenPageHasNoPendingPaymentCandidate_DoesNotCallPayment()
    {
        var userId = Guid.NewGuid();
        var repository = Substitute.For<IBookingRepository>();
        repository.ListPassengerHistoryAsync(userId, null, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<BookingEntity>.Create([], 1, 20, 0));
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        var handler = new GetBookingHistoryQueryHandler(repository, paymentLookup);

        var result = await handler.Handle(
            new GetBookingHistoryQuery(userId, null, null, null, 1, 20),
            CancellationToken.None);

        result.Items.Should().BeEmpty();
        await paymentLookup.DidNotReceiveWithAnyArgs().LookupAsync(default, default!, default);
    }

    [Fact]
    public async Task Handle_WhenPaymentLookupThrowsTransportFailure_FailsOpen()
    {
        var userId = Guid.NewGuid();
        var booking = CreatePendingBooking(userId, 350_000);
        var repository = Substitute.For<IBookingRepository>();
        repository.ListPassengerHistoryAsync(userId, null, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<BookingEntity>.Create([booking], 1, 20, 1));
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        paymentLookup.LookupAsync(userId, Arg.Any<IReadOnlyCollection<PaymentRedirectLookupReference>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<PaymentRedirectLookupItem>>>(_ => throw new HttpRequestException("unavailable"));
        var handler = new GetBookingHistoryQueryHandler(repository, paymentLookup);

        var result = await handler.Handle(
            new GetBookingHistoryQuery(userId, null, null, null, 1, 20),
            CancellationToken.None);

        result.Items.Should().ContainSingle().Which.PaymentRedirectUrl.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenCallerCancels_PropagatesCancellation()
    {
        var userId = Guid.NewGuid();
        var booking = CreatePendingBooking(userId, 350_000);
        var repository = Substitute.For<IBookingRepository>();
        repository.ListPassengerHistoryAsync(userId, null, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<BookingEntity>.Create([booking], 1, 20, 1));
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        using var source = new CancellationTokenSource();
        source.Cancel();
        paymentLookup.LookupAsync(userId, Arg.Any<IReadOnlyCollection<PaymentRedirectLookupReference>>(), source.Token)
            .Returns<Task<IReadOnlyList<PaymentRedirectLookupItem>>>(_ => throw new OperationCanceledException(source.Token));
        var handler = new GetBookingHistoryQueryHandler(repository, paymentLookup);

        var action = () => handler.Handle(
            new GetBookingHistoryQuery(userId, null, null, null, 1, 20),
            source.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html")]
    public void BookingHistoryDto_PublicAndInternalContractsAlwaysSerializePaymentRedirectUrl(string? url)
    {
        var dto = new BookingHistoryItemDto(
            Guid.NewGuid(),
            "VR-20260801-ABCDEFGH",
            Guid.NewGuid(),
            "PENDING_PAYMENT",
            DateTimeOffset.UtcNow,
            350_000,
            "Origin",
            "Destination",
            DateTimeOffset.UtcNow.AddDays(1),
            null,
            null,
            "Route",
            [],
            url);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(dto, options));

        document.RootElement.TryGetProperty("paymentRedirectUrl", out var property).Should().BeTrue();
        if (url is null)
            property.ValueKind.Should().Be(JsonValueKind.Null);
        else
            property.GetString().Should().Be(url);
        typeof(BookingsController).GetMethod(nameof(BookingsController.GetHistoryAsync))!.ReturnType
            .Should().Be(typeof(Task<ActionResult<PagedResult<BookingHistoryItemDto>>>));
        typeof(InternalBookingsController).GetMethod(nameof(InternalBookingsController.GetHistoryAsync))!.ReturnType
            .Should().Be(typeof(Task<ActionResult<PagedResult<BookingHistoryItemDto>>>));
    }

    [Fact]
    public void Query_UsesReadOnlyCqrsMarker()
    {
        var query = new GetBookingHistoryQuery(Guid.NewGuid(), null, null, null, 1, 20);

        query.Should().BeAssignableTo<IQuery<PagedResult<BookingHistoryItemDto>>>();
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

    private static BookingEntity CreatePendingBooking(
        Guid userId,
        long totalAmount,
        Guid? bookingGroupId = null,
        TripDirection? tripDirection = null)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(DateTimeOffset.UtcNow),
            userId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            Money.FromRaw(totalAmount),
            Money.Zero,
            Money.FromRaw(totalAmount),
            "Origin",
            "Destination",
            DateTimeOffset.UtcNow.AddDays(1),
            "Route");
        if (bookingGroupId.HasValue && tripDirection.HasValue)
            booking.AssignRoundTripGroup(bookingGroupId.Value, tripDirection.Value);

        return booking;
    }

    private static PaymentRedirectLookupItem LookupItem(
        string referenceType,
        Guid referenceId,
        long amount,
        string url)
        => new(
            Guid.NewGuid(),
            referenceType,
            referenceId,
            amount,
            DateTimeOffset.UtcNow.AddMinutes(5),
            url);
}
