using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Internal.Bookings;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.UnitTests.Features.Internal.Bookings;

public sealed class GetTripNotificationRecipientsQueryHandlerTests
{
    [Fact]
    public async Task ValidTripDelegatesToReadProjectionWithoutWrites()
    {
        var tripId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var expected = new TripNotificationRecipientsDto(
            tripId,
            [
                new(
                    Guid.Parse("22222222-2222-4222-8222-222222222222"),
                    Guid.Parse("33333333-3333-4333-8333-333333333333"),
                    "CONFIRMED"),
            ]);
        var repository = Substitute.For<IBookingRepository>();
        repository.GetTripNotificationRecipientsAsync(
                tripId,
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var handler = new GetTripNotificationRecipientsQueryHandler(repository);

        var result = await handler.Handle(
            new GetTripNotificationRecipientsQuery(tripId.ToString("D")),
            CancellationToken.None);

        result.Should().BeSameAs(expected);
        await repository.Received(1).GetTripNotificationRecipientsAsync(
            tripId,
            Arg.Any<CancellationToken>());
        await repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        repository.DidNotReceiveWithAnyArgs().Update(default!);
        repository.DidNotReceiveWithAnyArgs().Remove(default!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidTripReturnsValidationErrorWithoutQuery(string tripId)
    {
        var repository = Substitute.For<IBookingRepository>();
        var handler = new GetTripNotificationRecipientsQueryHandler(repository);

        var act = () => handler.Handle(
            new GetTripNotificationRecipientsQuery(tripId),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        await repository.DidNotReceiveWithAnyArgs()
            .GetTripNotificationRecipientsAsync(default, default);
    }
}
