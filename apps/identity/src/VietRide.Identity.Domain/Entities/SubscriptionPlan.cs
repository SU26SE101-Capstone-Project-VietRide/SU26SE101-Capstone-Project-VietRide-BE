using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Domain.Entities;

public sealed class SubscriptionPlan : BaseEntity<Guid>, IActivatable
{
    public static readonly Guid StarterPlanId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Money PricePerMonth { get; private set; } = Money.Zero;
    public Money PricePerYear { get; private set; } = Money.Zero;
    public int MaxVehicles { get; private set; }
    public int MaxDrivers { get; private set; }
    public int MaxAssistants { get; private set; }
    public int MaxOperatorUsers { get; private set; }
    public int MaxRoutes { get; private set; }
    public int MaxTripsPerMonth { get; private set; }
    public bool EnableParcel { get; private set; }
    public bool EnableShuttle { get; private set; }
    public bool EnableRag { get; private set; }
    public bool IsActive { get; private set; } = true;

    private SubscriptionPlan() { }

    public static SubscriptionPlan CreateStarter()
    {
        return new SubscriptionPlan
        {
            Id = StarterPlanId,
            Name = "Starter (Free Trial)",
            Description = "Default onboarding plan seeded by Identity migration.",
            PricePerMonth = Money.Zero,
            PricePerYear = Money.Zero,
            MaxVehicles = 3,
            MaxDrivers = 5,
            MaxAssistants = 5,
            MaxOperatorUsers = 3,
            MaxRoutes = 5,
            MaxTripsPerMonth = 100,
            EnableParcel = false,
            EnableShuttle = false,
            EnableRag = true,
            IsActive = true,
        };
    }

    public static SubscriptionPlan Create(
        string name,
        string? description,
        Money pricePerMonth,
        Money pricePerYear,
        int maxVehicles,
        int maxDrivers,
        int maxAssistants,
        int maxOperatorUsers,
        int maxRoutes,
        int maxTripsPerMonth,
        bool enableParcel,
        bool enableShuttle,
        bool enableRag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureNonNegative(maxVehicles, nameof(maxVehicles));
        EnsureNonNegative(maxDrivers, nameof(maxDrivers));
        EnsureNonNegative(maxAssistants, nameof(maxAssistants));
        EnsureNonNegative(maxOperatorUsers, nameof(maxOperatorUsers));
        EnsureNonNegative(maxRoutes, nameof(maxRoutes));
        EnsureNonNegative(maxTripsPerMonth, nameof(maxTripsPerMonth));

        return new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            PricePerMonth = pricePerMonth,
            PricePerYear = pricePerYear,
            MaxVehicles = maxVehicles,
            MaxDrivers = maxDrivers,
            MaxAssistants = maxAssistants,
            MaxOperatorUsers = maxOperatorUsers,
            MaxRoutes = maxRoutes,
            MaxTripsPerMonth = maxTripsPerMonth,
            EnableParcel = enableParcel,
            EnableShuttle = enableShuttle,
            EnableRag = enableRag,
            IsActive = true,
        };
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static void EnsureNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }
    }
}
