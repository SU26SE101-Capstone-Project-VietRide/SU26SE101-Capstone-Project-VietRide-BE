using FluentAssertions;
using VietRide.Trip.Application.Features.Locations;

namespace VietRide.Trip.UnitTests.Features.Locations;

public sealed class UpdateLocationValidatorTests
{
    private readonly UpdateLocationValidator validator = new();

    [Fact]
    public void Validate_WhenOnlyNameAndSortOrderAreProvided_ShouldSucceed()
    {
        var command = new UpdateLocationCommand(
            Guid.NewGuid(),
            null,
            "Updated location",
            null,
            25,
            null);

        var result = validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenOptionalFieldIsProvidedButEmpty_ShouldFail()
    {
        var command = new UpdateLocationCommand(
            Guid.NewGuid(),
            "",
            null,
            null,
            null,
            null);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(command.Code));
    }
}
