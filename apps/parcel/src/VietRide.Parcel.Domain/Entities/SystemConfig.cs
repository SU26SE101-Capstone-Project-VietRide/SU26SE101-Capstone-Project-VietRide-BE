using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class SystemConfig : BaseEntity<Guid>
{
    public string Key { get; private set; } = string.Empty;
    public decimal DecimalValue { get; private set; }
    public int Version { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }

    private SystemConfig() { }

    public static SystemConfig Create(
        string key,
        decimal decimalValue,
        int version,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Config key is required.", nameof(key));
        }

        return new SystemConfig
        {
            Id = Guid.NewGuid(),
            Key = key.Trim().ToUpperInvariant(),
            DecimalValue = decimalValue,
            Version = version,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            IsActive = true,
        };
    }
}
