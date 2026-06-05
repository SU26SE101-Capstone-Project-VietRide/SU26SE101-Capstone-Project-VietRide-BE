using FluentAssertions;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.UnitTests.Domain;

public sealed class UserDeviceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NewUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reactivate_SetsActiveAndBumpsLastActiveAt()
    {
        var device = UserDevice.Create(UserId, "token-x", DevicePlatform.ANDROID, CreatedAt);
        device.Deactivate();

        device.Reactivate(Now);

        device.UserId.Should().Be(UserId);
        device.IsActive.Should().BeTrue();
        device.LastActiveAt.Should().Be(Now);
    }

    [Fact]
    public void ClaimBy_ReassignsUserReactivatesAndBumpsLastActiveAt()
    {
        var device = UserDevice.Create(UserId, "token-x", DevicePlatform.IOS, CreatedAt);
        device.Deactivate();

        device.ClaimBy(NewUserId, Now);

        device.UserId.Should().Be(NewUserId);
        device.IsActive.Should().BeTrue();
        device.LastActiveAt.Should().Be(Now);
    }

    [Fact]
    public void ClaimBy_WhenNewUserIdEmpty_ThrowsArgumentException()
    {
        var device = UserDevice.Create(UserId, "token-x", DevicePlatform.WEB, CreatedAt);

        var act = () => device.ClaimBy(Guid.Empty, Now);

        act.Should().Throw<ArgumentException>();
        device.UserId.Should().Be(UserId);
    }
}
