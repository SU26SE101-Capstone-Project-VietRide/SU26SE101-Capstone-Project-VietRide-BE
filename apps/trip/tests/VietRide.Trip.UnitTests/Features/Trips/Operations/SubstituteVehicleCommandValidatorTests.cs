using FluentAssertions;
using VietRide.Trip.Application.Features.Trips.Operations;

namespace VietRide.Trip.UnitTests.Features.Trips.Operations;

public sealed class SubstituteVehicleCommandValidatorTests
{
    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void Validate_MissingRequiredIncidentOrCrew_IsInvalid(
        bool crewSpecified,
        bool hasDriver,
        bool hasAssistant,
        bool hasIncident)
    {
        var command = new SubstituteVehicleCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(1),
            "Xe hỏng",
            hasIncident ? Guid.NewGuid() : null,
            true,
            hasDriver ? Guid.NewGuid() : null,
            hasAssistant ? Guid.NewGuid() : null,
            crewSpecified);

        var result = new SubstituteVehicleCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_CompleteReplacementRequest_IsValid()
    {
        var command = new SubstituteVehicleCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(1),
            "Xe hỏng",
            Guid.NewGuid(),
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            true);

        new SubstituteVehicleCommandValidator().Validate(command).IsValid.Should().BeTrue();
    }
}
