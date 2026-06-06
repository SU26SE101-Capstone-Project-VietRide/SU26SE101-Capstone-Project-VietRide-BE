using System.Reflection;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Devices.GetActiveDeviceTokens;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.UnitTests.Application.Devices;

public sealed class GetActiveDeviceTokensQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsTokensFromActiveDeviceLookup()
    {
        var userId = Guid.NewGuid();
        var repositoryProxy = UserDeviceRepositoryProxy.Create(new[]
        {
            CreateDevice("active-token-1", "ANDROID"),
            CreateDevice("active-token-2", "IOS"),
        });
        var handler = new GetActiveDeviceTokensQueryHandler(repositoryProxy.Repository);

        var result = await handler.Handle(new GetActiveDeviceTokensQuery(userId), CancellationToken.None);

        Assert.Collection(
            result,
            first =>
            {
                Assert.Equal("active-token-1", first.FcmToken);
                Assert.Equal("ANDROID", first.Platform);
            },
            second =>
            {
                Assert.Equal("active-token-2", second.FcmToken);
                Assert.Equal("IOS", second.Platform);
            });
        Assert.Equal(userId, repositoryProxy.Proxy.LastUserId);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenUserHasNoActiveDevices()
    {
        var repositoryProxy = UserDeviceRepositoryProxy.Create(Array.Empty<UserDevice>());
        var handler = new GetActiveDeviceTokensQueryHandler(repositoryProxy.Repository);

        var result = await handler.Handle(new GetActiveDeviceTokensQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result);
    }

    private static UserDevice CreateDevice(string fcmToken, string platform)
    {
        var device = (UserDevice)Activator.CreateInstance(typeof(UserDevice), nonPublic: true)!;
        SetProperty(device, "FcmToken", fcmToken);

        var platformProperty = typeof(UserDevice).GetProperty("Platform")!;
        var platformValue = Enum.Parse(platformProperty.PropertyType, platform, ignoreCase: true);
        SetProperty(device, "Platform", platformValue);

        return device;
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(propertyName)!;
        if (property.SetMethod is not null)
        {
            property.SetValue(instance, value);
            return;
        }

        var backingField = instance.GetType().GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        backingField.SetValue(instance, value);
    }

    private class UserDeviceRepositoryProxy : DispatchProxy
    {
        private IReadOnlyList<UserDevice> _devices = Array.Empty<UserDevice>();

        public Guid? LastUserId { get; private set; }

        public static (IUserDeviceRepository Repository, UserDeviceRepositoryProxy Proxy) Create(IReadOnlyList<UserDevice> devices)
        {
            var repository = Create<IUserDeviceRepository, UserDeviceRepositoryProxy>();
            var proxy = (UserDeviceRepositoryProxy)(object)repository;
            proxy._devices = devices;
            return (repository, proxy);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "ListActiveByUserIdAsync")
            {
                LastUserId = (Guid)args![0]!;
                return Task.FromResult(_devices);
            }

            throw new NotSupportedException($"Unexpected repository call: {targetMethod?.Name}");
        }
    }
}
