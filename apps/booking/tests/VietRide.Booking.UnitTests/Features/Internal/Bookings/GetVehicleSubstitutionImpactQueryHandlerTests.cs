using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Internal.Bookings;

namespace VietRide.Booking.UnitTests.Features.Internal.Bookings;

public sealed class GetVehicleSubstitutionImpactQueryHandlerTests
{
    [Fact]
    public async Task FiltersTripOperatorBookingAndPassengerEligibilityWithoutWrites()
    {
        var tripId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var operatorId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var expected = new VehicleSubstitutionImpactDto(
            tripId,
            operatorId,
            [
                new VehicleSubstitutionImpactDto.BookingImpact(
                    Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"),
                    "CONFIRMED",
                    [
                        new VehicleSubstitutionImpactDto.PassengerImpact(
                            Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"),
                            "BOARDED",
                            "A01"),
                        new VehicleSubstitutionImpactDto.PassengerImpact(
                            Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"),
                            "PENDING",
                            "A02"),
                    ]),
                new VehicleSubstitutionImpactDto.BookingImpact(
                    Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff"),
                    "PARTIAL_NO_SHOW",
                    []),
            ]);
        var repository = Substitute.For<IBookingRepository>();
        repository.GetVehicleSubstitutionImpactAsync(
                tripId,
                operatorId,
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var handler = new GetVehicleSubstitutionImpactQueryHandler(repository);

        var result = await handler.Handle(
            new GetVehicleSubstitutionImpactQuery(
                tripId.ToString("D"),
                operatorId.ToString("D")),
            CancellationToken.None);

        result.Should().BeSameAs(expected);
        await repository.Received(1).GetVehicleSubstitutionImpactAsync(
            tripId,
            operatorId,
            Arg.Any<CancellationToken>());
        await repository.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
        repository.DidNotReceiveWithAnyArgs().Update(default!);
        repository.DidNotReceiveWithAnyArgs().Remove(default!);
    }

    [Fact]
    public async Task ForeignOperatorAndNoMatchReturnEmptyOrderedSnapshot()
    {
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var foreignOperatorId = Guid.NewGuid();
        var repository = Substitute.For<IBookingRepository>();
        repository.GetVehicleSubstitutionImpactAsync(
                tripId,
                operatorId,
                Arg.Any<CancellationToken>())
            .Returns(new VehicleSubstitutionImpactDto(
                tripId,
                operatorId,
                [
                    new VehicleSubstitutionImpactDto.BookingImpact(
                        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
                        "CONFIRMED",
                        []),
                    new VehicleSubstitutionImpactDto.BookingImpact(
                        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
                        "PARTIAL_NO_SHOW",
                        []),
                ]));
        repository.GetVehicleSubstitutionImpactAsync(
                tripId,
                foreignOperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new VehicleSubstitutionImpactDto(
                tripId,
                foreignOperatorId,
                []));
        var handler = new GetVehicleSubstitutionImpactQueryHandler(repository);

        var matching = await handler.Handle(
            new GetVehicleSubstitutionImpactQuery(
                tripId.ToString("D"),
                operatorId.ToString("D")),
            CancellationToken.None);
        var foreign = await handler.Handle(
            new GetVehicleSubstitutionImpactQuery(
                tripId.ToString("D"),
                foreignOperatorId.ToString("D")),
            CancellationToken.None);

        matching.Bookings.Select(booking => booking.BookingId).Should()
            .BeInAscendingOrder();
        foreign.OldTripId.Should().Be(tripId);
        foreign.OperatorId.Should().Be(foreignOperatorId);
        foreign.Bookings.Should().BeEmpty();
    }

    [Fact]
    public async Task IncludesChainedSubstitutionPassengerWithNullOriginalSeat()
    {
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var passengerId = Guid.NewGuid();
        var expected = new VehicleSubstitutionImpactDto(
            tripId,
            operatorId,
            [
                new VehicleSubstitutionImpactDto.BookingImpact(
                    Guid.NewGuid(),
                    "CONFIRMED",
                    [
                        new VehicleSubstitutionImpactDto.PassengerImpact(
                            passengerId,
                            "PENDING",
                            null),
                    ]),
            ]);
        var repository = Substitute.For<IBookingRepository>();
        repository.GetVehicleSubstitutionImpactAsync(
                tripId,
                operatorId,
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var handler = new GetVehicleSubstitutionImpactQueryHandler(repository);

        var result = await handler.Handle(
            new GetVehicleSubstitutionImpactQuery(
                tripId.ToString("D"),
                operatorId.ToString("D")),
            CancellationToken.None);

        result.Bookings.Should().ContainSingle();
        result.Bookings[0].Passengers.Should().ContainSingle();
        result.Bookings[0].Passengers[0].Should().BeEquivalentTo(new
        {
            PassengerId = passengerId,
            BoardingStatus = "PENDING",
            OriginalSeatNumber = (string?)null,
        });
    }
}
