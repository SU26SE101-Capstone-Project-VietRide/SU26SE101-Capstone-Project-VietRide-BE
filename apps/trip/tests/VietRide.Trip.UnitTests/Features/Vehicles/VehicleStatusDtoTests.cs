using System.Text.Json;
using VietRide.Trip.Application.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Features.Vehicles;

public sealed class VehicleStatusDtoTests
{
    [Fact]
    public void Serialize_UsesContractString()
    {
        var json = JsonSerializer.Serialize(VehicleStatusDto.ACTIVE);

        Assert.Equal("\"ACTIVE\"", json);
    }

    [Fact]
    public void Deserialize_FromContractString_BindsEnum()
    {
        var status = JsonSerializer.Deserialize<VehicleStatusDto>("\"MAINTENANCE\"");

        Assert.Equal(VehicleStatusDto.MAINTENANCE, status);
    }
}
