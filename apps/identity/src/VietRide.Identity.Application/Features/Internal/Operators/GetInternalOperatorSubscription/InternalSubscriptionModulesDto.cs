namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;

public sealed record InternalSubscriptionModulesDto(
    bool EnableParcel,
    bool EnableShuttle,
    bool EnableRag);
