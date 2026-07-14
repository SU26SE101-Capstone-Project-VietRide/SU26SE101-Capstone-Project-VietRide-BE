using FluentAssertions;
using VietRide.Trip.Application.Features.Shuttle;

namespace VietRide.Trip.UnitTests.Features.Shuttle;

public sealed class CreateShuttleTripCommandValidatorTests
{
    private readonly CreateShuttleTripCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ValidSubsetAndSchedule_Passes()
    {
        var now = DateTimeOffset.UtcNow;
        var result = await _validator.ValidateAsync(new CreateShuttleTripCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            now,
            now.AddMinutes(30),
            [Guid.NewGuid(), Guid.NewGuid()],
            "Morning shuttle"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_DuplicateBookingOrInvalidSchedule_Fails()
    {
        var bookingId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var result = await _validator.ValidateAsync(new CreateShuttleTripCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            now,
            now,
            [bookingId, bookingId],
            null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateShuttleTripCommand.ScheduledEndTime));
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateShuttleTripCommand.OrderedBookingIds));
    }
}
