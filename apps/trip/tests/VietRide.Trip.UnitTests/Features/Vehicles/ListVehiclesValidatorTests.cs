using VietRide.Trip.Application.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Features.Vehicles;

public sealed class ListVehiclesValidatorTests
{
    [Fact]
    public void Validate_WithSupportedFields_IsValid()
    {
        var result = new ListVehiclesValidator().Validate(
            new ListVehiclesQuery(Guid.NewGuid(), 1, 20, "51A", "licensePlate", "updatedAt", "desc"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithUnsupportedFields_IsInvalid()
    {
        var result = new ListVehiclesValidator().Validate(
            new ListVehiclesQuery(Guid.NewGuid(), 1, 20, "x", "status", "operatorId", "sideways"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WithInvalidSortByOnly_RemainsValidForCodedBadRequestHandling()
    {
        var result = new ListVehiclesValidator().Validate(
            new ListVehiclesQuery(Guid.NewGuid(), 1, 20, null, null, "operatorId", null));

        Assert.True(result.IsValid);
    }
}
