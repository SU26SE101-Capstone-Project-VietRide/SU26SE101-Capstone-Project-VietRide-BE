using FluentAssertions;
using VietRide.Trip.Application.Features.DriverSchedules;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

public sealed class UpdateDriverScheduleValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("FUTURE")]
    [InlineData("ALL")]
    public void InvalidApplyTo_IsRejected(string applyTo)
    {
        var result = new UpdateDriverScheduleValidator().Validate(ValidCommand() with { ApplyTo = applyTo });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateDriverScheduleCommand.ApplyTo));
    }

    [Fact]
    public void EmptyPatch_IsRejected()
    {
        var command = ValidCommand() with { DepartureTimeSpecified = false, DepartureTime = null };

        new UpdateDriverScheduleValidator().Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void NullForNonNullableField_IsRejected()
    {
        var command = ValidCommand() with { DepartureTime = null };

        var result = new UpdateDriverScheduleValidator().Validate(command);

        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateDriverScheduleCommand.DepartureTime));
    }

    [Fact]
    public void ValidDaysAndExplicitNullableFields_AreAccepted()
    {
        var command = ValidCommand() with
        {
            DepartureTimeSpecified = false,
            DepartureTime = null,
            DayOfWeekSpecified = true,
            DayOfWeek = [1, 3, 7],
            AssistantUserIdSpecified = true,
            AssistantUserId = null,
            VehicleIdSpecified = true,
            VehicleId = null,
            ValidUntilSpecified = true,
            ValidUntil = null,
        };

        new UpdateDriverScheduleValidator().Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void FutureOnlyBaseFareSetAndClear_AreAccepted()
    {
        var set = ValidCommand() with
        {
            DepartureTimeSpecified = false,
            DepartureTime = null,
            BaseFareSpecified = true,
            BaseFare = 400_000,
        };
        var clear = set with { BaseFare = null };

        new UpdateDriverScheduleValidator().Validate(set).IsValid.Should().BeTrue();
        new UpdateDriverScheduleValidator().Validate(clear).IsValid.Should().BeTrue();
    }

    [Fact]
    public void NegativeOrAllPendingBaseFare_IsRejected()
    {
        var negative = ValidCommand() with
        {
            DepartureTimeSpecified = false,
            DepartureTime = null,
            BaseFareSpecified = true,
            BaseFare = -1,
        };
        var allPending = negative with
        {
            ApplyTo = UpdateDriverScheduleCommand.AllPending,
            BaseFare = 400_000,
        };

        new UpdateDriverScheduleValidator().Validate(negative).Errors
            .Should().Contain(error => error.PropertyName == nameof(UpdateDriverScheduleCommand.BaseFare));
        new UpdateDriverScheduleValidator().Validate(allPending).Errors
            .Should().Contain(error => error.PropertyName == nameof(UpdateDriverScheduleCommand.BaseFare));
    }

    private static UpdateDriverScheduleCommand ValidCommand() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "request-1",
        UpdateDriverScheduleCommand.FutureOnly,
        true, new TimeOnly(8, 30),
        false, null,
        false, null,
        false, null,
        false, null,
        false, null,
        false, null,
        false, null);
}
