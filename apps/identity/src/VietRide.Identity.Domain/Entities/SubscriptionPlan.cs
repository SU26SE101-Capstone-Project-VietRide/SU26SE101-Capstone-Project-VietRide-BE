using VietRide.Identity.Domain.Enums;
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
    public SubscriptionPlanType PlanType { get; private set; } = SubscriptionPlanType.STANDARD;
    public Guid? OwnerOperatorId { get; private set; }
    public Guid? SourceCustomRequestId { get; private set; }
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
            PlanType = SubscriptionPlanType.STANDARD,
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
            PlanType = SubscriptionPlanType.STANDARD,
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

    public static SubscriptionPlan CreateCustom(
        Guid ownerOperatorId,
        Guid sourceCustomRequestId,
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
        if (ownerOperatorId == Guid.Empty || sourceCustomRequestId == Guid.Empty)
            throw new ArgumentException("Custom plan owner and source request are required.");
        if (pricePerMonth.Amount <= 0 && pricePerYear.Amount <= 0)
            throw new ArgumentException("At least one custom plan price must be payable.");

        var plan = Create(
            name,
            description,
            pricePerMonth,
            pricePerYear,
            maxVehicles,
            maxDrivers,
            maxAssistants,
            maxOperatorUsers,
            maxRoutes,
            maxTripsPerMonth,
            enableParcel,
            enableShuttle,
            enableRag);
        plan.PlanType = SubscriptionPlanType.CUSTOM;
        plan.OwnerOperatorId = ownerOperatorId;
        plan.SourceCustomRequestId = sourceCustomRequestId;
        return plan;
    }

    public void Activate()
    {
        if (PlanType == SubscriptionPlanType.CUSTOM && !IsActive)
            throw new InvalidOperationException("A deactivated custom plan cannot be reactivated.");
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;

    public void Update(
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
        bool enableRag,
        bool isActive)
    {
        if (PlanType == SubscriptionPlanType.CUSTOM)
        {
            if (isActive || !MatchesImmutableValues(
                    name,
                    description,
                    pricePerMonth,
                    pricePerYear,
                    maxVehicles,
                    maxDrivers,
                    maxAssistants,
                    maxOperatorUsers,
                    maxRoutes,
                    maxTripsPerMonth,
                    enableParcel,
                    enableShuttle,
                    enableRag))
            {
                throw new InvalidOperationException("Custom plan terms are immutable; only deactivation is allowed.");
            }

            IsActive = false;
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureNonNegative(maxVehicles, nameof(maxVehicles));
        EnsureNonNegative(maxDrivers, nameof(maxDrivers));
        EnsureNonNegative(maxAssistants, nameof(maxAssistants));
        EnsureNonNegative(maxOperatorUsers, nameof(maxOperatorUsers));
        EnsureNonNegative(maxRoutes, nameof(maxRoutes));
        EnsureNonNegative(maxTripsPerMonth, nameof(maxTripsPerMonth));

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        PricePerMonth = pricePerMonth;
        PricePerYear = pricePerYear;
        MaxVehicles = maxVehicles;
        MaxDrivers = maxDrivers;
        MaxAssistants = maxAssistants;
        MaxOperatorUsers = maxOperatorUsers;
        MaxRoutes = maxRoutes;
        MaxTripsPerMonth = maxTripsPerMonth;
        EnableParcel = enableParcel;
        EnableShuttle = enableShuttle;
        EnableRag = enableRag;
        IsActive = isActive;
    }

    public bool IsVisibleTo(Guid operatorId)
        => PlanType == SubscriptionPlanType.STANDARD || OwnerOperatorId == operatorId;

    private bool MatchesImmutableValues(
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
        => string.Equals(Name, name.Trim(), StringComparison.Ordinal)
            && string.Equals(Description, string.IsNullOrWhiteSpace(description) ? null : description.Trim(), StringComparison.Ordinal)
            && PricePerMonth == pricePerMonth
            && PricePerYear == pricePerYear
            && MaxVehicles == maxVehicles
            && MaxDrivers == maxDrivers
            && MaxAssistants == maxAssistants
            && MaxOperatorUsers == maxOperatorUsers
            && MaxRoutes == maxRoutes
            && MaxTripsPerMonth == maxTripsPerMonth
            && EnableParcel == enableParcel
            && EnableShuttle == enableShuttle
            && EnableRag == enableRag;

    private static void EnsureNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }
    }
}
