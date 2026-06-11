using VietRide.Trip.Application.Features.VehicleTypes;

namespace VietRide.Trip.UnitTests.Features.VehicleTypes;

public sealed class ListVehicleTypesValidatorTests
{
    [Fact]
    public void Validate_WithSupportedFields_IsValid()
    {
        var result = new ListVehicleTypesValidator().Validate(
            new ListVehicleTypesQuery(1, 20, "bus", "code, displayName", "defaultSeatCount", "asc"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithUnsupportedFields_IsInvalid()
    {
        var result = new ListVehicleTypesValidator().Validate(
            new ListVehicleTypesQuery(1, 20, "bus", "code,operatorId", "operatorId", "sideways"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WithInvalidSortByOnly_RemainsValidForCodedBadRequestHandling()
    {
        var result = new ListVehicleTypesValidator().Validate(
            new ListVehicleTypesQuery(1, 20, null, null, "operatorId", null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithEmptyCommaSeparatedSearchField_IsInvalid()
    {
        var result = new ListVehicleTypesValidator().Validate(
            new ListVehicleTypesQuery(1, 20, "bus", "code,,displayName", null, null));

        Assert.False(result.IsValid);
    }
}
