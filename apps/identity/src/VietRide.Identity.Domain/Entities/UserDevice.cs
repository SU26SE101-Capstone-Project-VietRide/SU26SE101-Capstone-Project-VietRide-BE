using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Domain.Entities;

public sealed class UserDevice : BaseEntity<Guid>, IActivatable
{
    public Guid UserId { get; private set; }
    public string FcmToken { get; private set; } = string.Empty;
    public DevicePlatform Platform { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset LastActiveAt { get; private set; }

    private UserDevice() { }

    public static UserDevice Create(
        Guid userId,
        string fcmToken,
        DevicePlatform platform,
        DateTimeOffset lastActiveAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fcmToken);

        return new UserDevice
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FcmToken = fcmToken,
            Platform = platform,
            IsActive = true,
            LastActiveAt = lastActiveAt,
        };
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void UpdateLastActive(DateTimeOffset lastActiveAt)
    {
        LastActiveAt = lastActiveAt;
    }
}
