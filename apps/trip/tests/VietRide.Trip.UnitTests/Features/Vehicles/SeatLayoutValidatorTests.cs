using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Features.Vehicles;

public sealed class SeatLayoutValidatorTests
{
    [Fact]
    public void UsablePassengerCapacity_ExcludesDisabledAndDriverAreaSeats()
    {
        var layout = CreateLayout(
            new SeatLayoutSeatDto("A1", 1, 1, 1, "STANDARD", false, false, false),
            new SeatLayoutSeatDto("A2", 1, 2, 1, "STANDARD", false, false, true),
            new SeatLayoutSeatDto("D1", 1, 3, 1, "DRIVER_AREA", false, false, false));

        SeatLayoutMetrics.CountUsablePassengerSeats(layout).Should().Be(1);
    }

    [Fact]
    public void Validate_RejectsCaseInsensitiveDuplicateSeatNumbers()
    {
        var layout = CreateLayout(
            new SeatLayoutSeatDto("A1", 1, 1, 1, "STANDARD", false, false, false),
            new SeatLayoutSeatDto("a1", 1, 2, 1, "STANDARD", false, false, false));

        FluentActions.Invoking(() => SeatLayoutValidator.Validate(layout, 2))
            .Should().Throw<ValidationException>();
    }

    [Fact]
    public void Validate_AllowsA1AndA01AsDistinctCodes()
    {
        var layout = CreateLayout(
            new SeatLayoutSeatDto("A1", 1, 1, 1, "STANDARD", false, false, false),
            new SeatLayoutSeatDto("A01", 1, 2, 1, "STANDARD", false, false, false));

        FluentActions.Invoking(() => SeatLayoutValidator.Validate(layout, 2))
            .Should().NotThrow();
    }

    private static SeatLayoutDto CreateLayout(params SeatLayoutSeatDto[] seats)
        => new(1, "STANDARD_BUS", seats.Length, 1, seats.Length, 1, [], seats);
}
