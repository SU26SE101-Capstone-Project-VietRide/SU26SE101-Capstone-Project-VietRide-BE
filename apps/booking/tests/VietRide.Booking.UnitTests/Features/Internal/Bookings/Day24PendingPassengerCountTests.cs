using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Internal.Bookings;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.UnitTests.Features.Internal.Bookings;

public sealed class Day24PendingPassengerCountTests
{
    private readonly IBookingRepository bookings = Substitute.For<IBookingRepository>();

    [Fact]
    public async Task Handle_ValidIds_ReturnsRawProjectionAndForwardsExactIds()
    {
        var tripId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var stopId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var operatorId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        using var cts = new CancellationTokenSource();
        bookings.GetPendingPassengerCountAsync(tripId, stopId, operatorId, cts.Token)
            .Returns(3);
        var sut = new GetPendingPassengerCountHandler(bookings);

        var result = await sut.Handle(
            new GetPendingPassengerCountQuery(
                tripId.ToString("D"),
                stopId.ToString("D"),
                operatorId.ToString("D")),
            cts.Token);

        result.Should().Be(new PendingPassengerCountDto(tripId, stopId, 3));
        await bookings.Received(1).GetPendingPassengerCountAsync(
            tripId,
            stopId,
            operatorId,
            cts.Token);
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task Handle_MalformedOrEmptyGuid_ThrowsValidationError(
        string tripId,
        string stopId,
        string? operatorId)
    {
        var sut = new GetPendingPassengerCountHandler(bookings);

        var act = () => sut.Handle(
            new GetPendingPassengerCountQuery(tripId, stopId, operatorId),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        await bookings.DidNotReceiveWithAnyArgs()
            .GetPendingPassengerCountAsync(default, default, default, default);
    }

    public static TheoryData<string, string, string?> InvalidInputs => new()
    {
        { "not-a-uuid", Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D") },
        { Guid.Empty.ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D") },
        { Guid.NewGuid().ToString("D"), "not-a-uuid", Guid.NewGuid().ToString("D") },
        { Guid.NewGuid().ToString("D"), Guid.Empty.ToString("D"), Guid.NewGuid().ToString("D") },
        { Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), "not-a-uuid" },
        { Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.Empty.ToString("D") },
        { Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), null },
    };
}
