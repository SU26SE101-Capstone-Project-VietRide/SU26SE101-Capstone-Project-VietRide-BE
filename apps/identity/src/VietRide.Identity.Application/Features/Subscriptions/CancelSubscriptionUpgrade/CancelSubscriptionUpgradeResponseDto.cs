namespace VietRide.Identity.Application.Features.Subscriptions.CancelSubscriptionUpgrade;

public sealed record CancelSubscriptionUpgradeResponseDto(Guid UpgradeAttemptId, string Status);
