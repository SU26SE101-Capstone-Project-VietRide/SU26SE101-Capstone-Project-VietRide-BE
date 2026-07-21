using FluentAssertions;
using FluentValidation.TestHelper;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.History;
using VietRide.Parcel.Application.Features.PassengerHistory;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.PassengerHistory;

public sealed class GetPassengerHistoryQueryHandlerTests
{
    [Fact]
    public async Task TicketBranch_CallsOnlyBookingAndMapsNestedTickets()
    {
        var userId = Guid.NewGuid();
        var bookingClient = Substitute.For<IBookingServiceClient>();
        var parcelRepository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var booking = new BookingHistoryItemDto(
            Guid.NewGuid(),
            "VR-20260701-ABCDEFGH",
            Guid.NewGuid(),
            "CONFIRMED",
            DateTimeOffset.UtcNow,
            350_000,
            "Origin",
            "Destination",
            DateTimeOffset.UtcNow.AddDays(1),
            null,
            null,
            "Route",
            [new BookingHistoryTicketDto(Guid.NewGuid(), "VT-20260701-ABCDEFGH", "A01", "ISSUED", 350_000)]);
        bookingClient.GetPassengerHistoryAsync(
                userId,
                "CONFIRMED",
                null,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(new BookingHistoryOutcome(
                true,
                new BookingHistoryPage([booking], 1, 20, 1, 1, false, false),
                null));
        var handler = new GetPassengerHistoryQueryHandler(
            bookingClient,
            new SentParcelHistoryReader(parcelRepository, tripClient));

        var result = await handler.Handle(
            new GetPassengerHistoryQuery(userId, "TICKET", "CONFIRMED", null, null, 1, 20),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items[0].Type.Should().Be("TICKET");
        result.Items[0].Ticket.Should().NotBeNull();
        result.Items[0].Parcel.Should().BeNull();
        result.Items[0].Ticket!.Tickets.Should().ContainSingle();
        await parcelRepository.DidNotReceive().ListSentByUserIdAsync(
            Arg.Any<Guid>(),
            Arg.Any<ParcelStatus?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ParcelBranch_CallsOnlySenderScopedLocalRepository()
    {
        var userId = Guid.NewGuid();
        var bookingClient = Substitute.For<IBookingServiceClient>();
        var parcelRepository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        parcelRepository.ListSentByUserIdAsync(
                userId,
                ParcelStatus.IN_TRANSIT,
                null,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([], 1, 20, 0));
        var handler = new GetPassengerHistoryQueryHandler(
            bookingClient,
            new SentParcelHistoryReader(parcelRepository, tripClient));

        var result = await handler.Handle(
            new GetPassengerHistoryQuery(userId, "PARCEL", "IN_TRANSIT", null, null, 1, 20),
            CancellationToken.None);

        result.Items.Should().BeEmpty();
        await bookingClient.DidNotReceive().GetPassengerHistoryAsync(
            Arg.Any<Guid>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TicketBranch_BookingFailure_Throws502Exception()
    {
        var bookingClient = Substitute.For<IBookingServiceClient>();
        bookingClient.GetPassengerHistoryAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new BookingHistoryOutcome(false, null, "offline"));
        var handler = new GetPassengerHistoryQueryHandler(
            bookingClient,
            new SentParcelHistoryReader(
                Substitute.For<IParcelRepository>(),
                Substitute.For<ITripServiceClient>()));

        var action = () => handler.Handle(
            new GetPassengerHistoryQuery(Guid.NewGuid(), "TICKET", null, null, null, 1, 20),
            CancellationToken.None);

        var exception = (await action.Should()
            .ThrowAsync<PassengerHistoryUpstreamUnavailableException>()).Which;
        exception.StatusCode.Should().Be(502);
        exception.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
    }

    [Theory]
    [InlineData("ALL", null)]
    [InlineData("TICKET", "IN_TRANSIT")]
    [InlineData("PARCEL", "CONFIRMED")]
    public void Validator_RejectsUnsupportedTypeOrBranchStatus(string type, string? status)
    {
        var validator = new GetPassengerHistoryQueryValidator();

        var result = validator.TestValidate(
            new GetPassengerHistoryQuery(Guid.NewGuid(), type, status, null, null, 1, 20));

        result.IsValid.Should().BeFalse();
    }
}
