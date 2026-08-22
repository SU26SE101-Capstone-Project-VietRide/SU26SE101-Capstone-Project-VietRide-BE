using FluentAssertions;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Shuttle;
using VietRide.Trip.UnitTests.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Features.Shuttle;

public sealed class ReassignShuttleTripTests
{
    [Fact]
    public async Task Validator_RequiresResourceAndReason()
    {
        var validator = new ReassignShuttleTripCommandValidator();
        var command = new ReassignShuttleTripCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            " ");

        var result = await validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("At least one", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(command.Reason));
    }

    [Fact]
    public async Task Validator_AllowsDriverOnlyOrVehicleOnlyReplacement()
    {
        var validator = new ReassignShuttleTripCommandValidator();
        var common = new ReassignShuttleTripCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Driver unavailable");

        var driverOnly = await validator.ValidateAsync(common);
        var vehicleOnly = await validator.ValidateAsync(common with
        {
            DriverUserId = null,
            VehicleId = Guid.NewGuid(),
        });

        driverOnly.IsValid.Should().BeTrue();
        vehicleOnly.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_ForwardsPartialAssignmentAndReason()
    {
        var command = new ReassignShuttleTripCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Driver unavailable");
        var expected = new ReassignShuttleTripResult(
            command.ShuttleTripId,
            command.DriverUserId!.Value,
            Guid.NewGuid());
        var service = TestProxy<IShuttleDispatchService>.Create((method, args) =>
        {
            if (method.Name != nameof(IShuttleDispatchService.ReassignAsync))
            {
                return null;
            }

            var input = Assert.IsType<ReassignShuttleTripInput>(args![0]);
            input.OperatorId.Should().Be(command.OperatorId);
            input.ShuttleTripId.Should().Be(command.ShuttleTripId);
            input.DriverUserId.Should().Be(command.DriverUserId);
            input.VehicleId.Should().BeNull();
            input.Reason.Should().Be(command.Reason);
            return expected;
        });
        var handler = new ReassignShuttleTripCommandHandler(service);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().Be(expected);
    }
}
