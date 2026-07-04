using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Infrastructure.Http;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public class DevIdentityServiceClientStubTests
{
    private readonly DevIdentityServiceClient _sut = new(NullLogger<DevIdentityServiceClient>.Instance);

    [Fact]
    public async Task GetUserInfoAsync_Returns_Success_With_Passenger_Role()
    {
        var userId = Guid.NewGuid();

        var result = await _sut.GetUserInfoAsync(userId);

        result.Kind.Should().Be(UserLookupOutcomeKind.Success);
        result.UserInfo.Should().NotBeNull();
        result.UserInfo!.Id.Should().Be(userId);
        result.UserInfo.Role.Should().Be("PASSENGER");
        result.UserInfo.Status.Should().Be("ACTIVE");
    }

    [Fact]
    public async Task GetOperatorInfoAsync_Returns_Success_With_Valid_Name()
    {
        var operatorId = Guid.NewGuid();

        var result = await _sut.GetOperatorInfoAsync(operatorId);

        result.Kind.Should().Be(OperatorLookupOutcomeKind.Success);
        result.OperatorInfo.Should().NotBeNull();
        result.OperatorInfo!.Id.Should().Be(operatorId);
        result.OperatorInfo.Name.Should().Be("Dev Operator");
    }
}
