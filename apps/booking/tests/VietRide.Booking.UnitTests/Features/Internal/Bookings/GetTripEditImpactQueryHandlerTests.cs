using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Internal.Bookings;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.UnitTests.Features.Internal.Bookings;

public sealed class GetTripEditImpactQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsRepositoryProjection_WithTrustedOperatorScope()
    {
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var expected = new TripEditImpactDto(
            tripId,
            2,
            [
                new TripEditImpactDto.ActiveBooking(
                    Guid.NewGuid(),
                    "PENDING_PAYMENT",
                    ["A01", "A02"]),
                new TripEditImpactDto.ActiveBooking(
                    Guid.NewGuid(),
                    "CONFIRMED",
                    ["B01"]),
            ]);
        var repository = Substitute.For<IBookingRepository>();
        repository.GetTripEditImpactAsync(tripId, operatorId, cancellation.Token)
            .Returns(expected);
        var handler = new GetTripEditImpactQueryHandler(repository);

        var result = await handler.Handle(
            new GetTripEditImpactQuery(tripId, operatorId),
            cancellation.Token);

        result.Should().BeSameAs(expected);
        await repository.Received(1).GetTripEditImpactAsync(
            tripId,
            operatorId,
            cancellation.Token);
    }

    [Fact]
    public async Task Handle_ThrowsValidationError_WhenOperatorIdIsEmpty()
    {
        var repository = Substitute.For<IBookingRepository>();
        var handler = new GetTripEditImpactQueryHandler(repository);

        Func<Task> act = () => handler.Handle(
            new GetTripEditImpactQuery(Guid.NewGuid(), Guid.Empty),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        await repository.DidNotReceiveWithAnyArgs()
            .GetTripEditImpactAsync(default, default, default);
    }
}
