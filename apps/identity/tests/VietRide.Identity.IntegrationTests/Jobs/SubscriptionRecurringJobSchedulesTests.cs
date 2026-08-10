using FluentAssertions;
using VietRide.Identity.Infrastructure.Jobs;

namespace VietRide.Identity.IntegrationTests.Jobs;

public sealed class SubscriptionRecurringJobSchedulesTests
{
    [Fact]
    public void TrialExpiryWarning_UsesEquivalentUtcSchedule()
    {
        SubscriptionLifecycleJob.WarningJobId.Should().Be("identity.subscription-warnings");
        SubscriptionRecurringJobSchedules.WarningCron.Should().Be("0 2 * * *");
    }

    [Fact]
    public void NeighboringSubscriptionJobs_RetainTheirExistingSchedules()
    {
        SubscriptionLifecycleJob.ExpiryJobId.Should().Be("identity.subscription-expiry");
        SubscriptionRecurringJobSchedules.ExpiryCron.Should().Be("30 17 * * *");

        SubscriptionLifecycleJob.RevertJobId.Should().Be("identity.subscription-auto-revert");
        SubscriptionRecurringJobSchedules.RevertCron.Should().Be("* * * * *");

        SubscriptionLifecycleJob.MonthlyResetJobId.Should().Be("identity.subscription-monthly-reset");
        SubscriptionRecurringJobSchedules.MonthlyResetCron.Should().Be("1 17 * * *");
    }
}
