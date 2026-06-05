using FluentAssertions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Devices.RemoveDeviceToken;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.UnitTests.Application.Devices;

public sealed class RemoveDeviceTokenCommandHandlerTests
{
    private static readonly Guid CallerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_ExistingCallerToken_DeactivatesRowAndRetainsIt()
    {
        var device = UserDevice.Create(CallerUserId, "token-delete", DevicePlatform.ANDROID, Now);
        var devices = new TestUserDeviceRepository(device);
        var handler = new RemoveDeviceTokenCommandHandler(devices);

        await handler.Handle(new RemoveDeviceTokenCommand(CallerUserId, "token-delete"), CancellationToken.None);

        devices.Items.Should().ContainSingle().Which.Should().BeSameAs(device);
        device.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TokenBelongsToAnotherUser_DoesNotDeactivateIt()
    {
        var device = UserDevice.Create(OtherUserId, "token-delete", DevicePlatform.IOS, Now);
        var devices = new TestUserDeviceRepository(device);
        var handler = new RemoveDeviceTokenCommandHandler(devices);

        await handler.Handle(new RemoveDeviceTokenCommand(CallerUserId, "token-delete"), CancellationToken.None);

        device.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_NullOrBlankToken_ReturnsWithoutRepositoryLookup(string? fcmToken)
    {
        var devices = new TestUserDeviceRepository();
        var handler = new RemoveDeviceTokenCommandHandler(devices);

        await handler.Handle(new RemoveDeviceTokenCommand(CallerUserId, fcmToken), CancellationToken.None);

        devices.FindByUserAndFcmTokenCallCount.Should().Be(0);
    }

    private sealed class TestUserDeviceRepository : IUserDeviceRepository
    {
        public TestUserDeviceRepository(params UserDevice[] devices)
        {
            Items = devices.ToList();
        }

        public List<UserDevice> Items { get; }
        public int FindByUserAndFcmTokenCallCount { get; private set; }

        public Task<UserDevice?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Items.FirstOrDefault(d => d.Id == id));

        public Task<UserDevice?> FindByUserAndFcmTokenAsync(Guid userId, string fcmToken, CancellationToken ct = default)
        {
            FindByUserAndFcmTokenCallCount++;
            return Task.FromResult(Items.FirstOrDefault(d => d.UserId == userId && d.FcmToken == fcmToken));
        }

        public Task<UserDevice?> FindByFcmTokenAsync(string fcmToken, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(d => d.FcmToken == fcmToken && d.IsActive));

        public Task<IReadOnlyList<UserDevice>> ListActiveByUserIdAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UserDevice>>(Items.Where(d => d.UserId == userId && d.IsActive).ToList());

        public Task<UserDevice> AddAsync(UserDevice entity, CancellationToken ct)
        {
            Items.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(UserDevice entity)
        {
        }

        public void Remove(UserDevice entity)
        {
            Items.Remove(entity);
        }

        public IQueryable<UserDevice> Query()
            => Items.AsQueryable();

        public IQueryable<UserDevice> QueryNoTracking()
            => Query();
    }
}
