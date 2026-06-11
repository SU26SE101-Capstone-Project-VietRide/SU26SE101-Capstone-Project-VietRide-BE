using FluentAssertions;
using VietRide.Trip.Application.Features.DriverSchedules;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

public sealed class CreateDriverScheduleValidatorTests
{
    [Fact]
    public async Task Validate_InvalidDaysAndDateWindow_ReturnsValidationErrors()
    {
        var command = new CreateDriverScheduleCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            [0, 8],
            new TimeOnly(8, 0),
            new DateOnly(2026, 6, 15),
            new DateOnly(2026, 6, 14));

        var result = await new CreateDriverScheduleValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName.StartsWith("DayOfWeek"));
        result.Errors.Should().Contain(error => error.PropertyName == "ValidUntil");
    }

    [Fact]
    public async Task Validate_EmptyDayOfWeek_ReturnsValidationError()
    {
        var command = new CreateDriverScheduleCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            [],
            new TimeOnly(8, 0),
            new DateOnly(2026, 6, 15),
            null);

        var result = await new CreateDriverScheduleValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "DayOfWeek");
    }
}
