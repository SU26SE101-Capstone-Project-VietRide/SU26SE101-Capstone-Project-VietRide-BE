using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Devices.RegisterDeviceToken;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.UnitTests.Application.Devices;

public sealed class RegisterDeviceTokenCommandHandlerTests
{
    private static readonly Guid CallerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset FrozenNow = new(2026, 6, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_NewToken_InsertsActiveDevice()
    {
        var devices = new TestUserDeviceRepository();
        var handler = CreateHandler(devices);

        var result = await handler.Handle(
            new RegisterDeviceTokenCommand(CallerUserId, "token-new", "ANDROID"),
            CancellationToken.None);

        devices.Items.Should().ContainSingle();
        var device = devices.Items.Single();
        device.UserId.Should().Be(CallerUserId);
        device.FcmToken.Should().Be("token-new");
        device.Platform.Should().Be(DevicePlatform.ANDROID);
        device.IsActive.Should().BeTrue();
        device.LastActiveAt.Should().Be(FrozenNow);
        result.DeviceId.Should().Be(device.Id);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SameUserSameActiveToken_RefreshesLastActiveWithoutInserting()
    {
        var originalLastActive = FrozenNow.AddDays(-1);
        var existing = UserDevice.Create(CallerUserId, "token-active", DevicePlatform.IOS, originalLastActive);
        var devices = new TestUserDeviceRepository(existing);
        var handler = CreateHandler(devices);

        await handler.Handle(
            new RegisterDeviceTokenCommand(CallerUserId, "token-active", "IOS"),
            CancellationToken.None);

        devices.Items.Should().ContainSingle().Which.Should().BeSameAs(existing);
        existing.IsActive.Should().BeTrue();
        existing.LastActiveAt.Should().Be(FrozenNow);
    }

    [Fact]
    public async Task Handle_SameUserLoggedOutToken_ReactivatesSameRowWithoutInserting()
    {
        var existing = UserDevice.Create(CallerUserId, "token-x", DevicePlatform.ANDROID, FrozenNow.AddDays(-2));
        existing.Deactivate();
        var devices = new TestUserDeviceRepository(existing);
        var handler = CreateHandler(devices);

        var result = await handler.Handle(
            new RegisterDeviceTokenCommand(CallerUserId, "token-x", "ANDROID"),
            CancellationToken.None);

        devices.Items.Should().ContainSingle().Which.Should().BeSameAs(existing);
        devices.AddCount.Should().Be(0);
        existing.IsActive.Should().BeTrue();
        existing.LastActiveAt.Should().Be(FrozenNow);
        result.DeviceId.Should().Be(existing.Id);
    }

    [Fact]
    public async Task Handle_ActiveTokenOwnedByAnotherUser_ClaimsExistingRow()
    {
        var existing = UserDevice.Create(OtherUserId, "token-claimed", DevicePlatform.WEB, FrozenNow.AddHours(-1));
        var devices = new TestUserDeviceRepository(existing);
        var handler = CreateHandler(devices);

        var result = await handler.Handle(
            new RegisterDeviceTokenCommand(CallerUserId, "token-claimed", "WEB"),
            CancellationToken.None);

        devices.Items.Should().ContainSingle().Which.Should().BeSameAs(existing);
        devices.AddCount.Should().Be(0);
        existing.UserId.Should().Be(CallerUserId);
        existing.IsActive.Should().BeTrue();
        existing.LastActiveAt.Should().Be(FrozenNow);
        result.DeviceId.Should().Be(existing.Id);
    }

    [Fact]
    public async Task Handle_InactiveCallerRowAndActiveOtherOwner_DeactivatesOtherOwnerBeforeReactivatingCaller()
    {
        var callerDevice = UserDevice.Create(CallerUserId, "token-edge", DevicePlatform.ANDROID, FrozenNow.AddDays(-2));
        callerDevice.Deactivate();
        var otherDevice = UserDevice.Create(OtherUserId, "token-edge", DevicePlatform.IOS, FrozenNow.AddHours(-1));
        var devices = new TestUserDeviceRepository(callerDevice, otherDevice);
        var handler = CreateHandler(devices);

        var result = await handler.Handle(
            new RegisterDeviceTokenCommand(CallerUserId, "token-edge", "ANDROID"),
            CancellationToken.None);

        devices.Items.Should().HaveCount(2);
        callerDevice.IsActive.Should().BeTrue();
        callerDevice.LastActiveAt.Should().Be(FrozenNow);
        otherDevice.IsActive.Should().BeFalse();
        devices.Items.Count(device => device.FcmToken == "token-edge" && device.IsActive).Should().Be(1);
        result.DeviceId.Should().Be(callerDevice.Id);
    }

    [Theory]
    [InlineData("ANDROID", true)]
    [InlineData("android", true)]
    [InlineData("500", false)]
    [InlineData("3", false)]
    [InlineData("WINDOWS", false)]
    public void Validator_ValidatesPlatformAgainstDefinedEnumNamesOnly(string platform, bool expectedIsValid)
    {
        var validator = new RegisterDeviceTokenCommandValidator();

        var result = validator.Validate(new RegisterDeviceTokenCommand(CallerUserId, "token-validator", platform));

        result.IsValid.Should().Be(expectedIsValid);
    }

    [Fact]
    public void Validator_RejectsFcmTokenLongerThan500Characters()
    {
        var validator = new RegisterDeviceTokenCommandValidator();

        var result = validator.Validate(new RegisterDeviceTokenCommand(CallerUserId, new string('a', 501), "ANDROID"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(RegisterDeviceTokenCommand.FcmToken));
    }

    private static RegisterDeviceTokenCommandHandler CreateHandler(IUserDeviceRepository devices)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FrozenNow);
        return new RegisterDeviceTokenCommandHandler(devices, clock);
    }

    private sealed class TestUserDeviceRepository : IUserDeviceRepository
    {
        public TestUserDeviceRepository(params UserDevice[] devices)
        {
            Items = devices.ToList();
        }

        public List<UserDevice> Items { get; }
        public int AddCount { get; private set; }

        public Task<UserDevice?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Items.FirstOrDefault(d => d.Id == id));

        public Task<UserDevice?> FindByUserAndFcmTokenAsync(Guid userId, string fcmToken, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(d => d.UserId == userId && d.FcmToken == fcmToken));

        public Task<UserDevice?> FindByFcmTokenAsync(string fcmToken, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(d => d.FcmToken == fcmToken && d.IsActive));

        public Task<IReadOnlyList<UserDevice>> ListActiveByUserIdAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UserDevice>>(Items.Where(d => d.UserId == userId && d.IsActive).ToList());

        public Task<UserDevice> AddAsync(UserDevice entity, CancellationToken ct)
        {
            AddCount++;
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
