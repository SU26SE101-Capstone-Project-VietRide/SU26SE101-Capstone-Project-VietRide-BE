using FluentAssertions;
using VietRide.Identity.Infrastructure.Jobs;

namespace VietRide.Identity.IntegrationTests.Jobs;

public sealed class SubscriptionRecurringJobSchedulesTests
{
    [Fact]
    public void TrialExpiryWarning_UsesDailyNineAmIctSchedule()
    {
        SubscriptionLifecycleJob.WarningJobId.Should().Be("identity.subscription-warnings");
        SubscriptionRecurringJobSchedules.WarningCron.Should().Be("0 9 * * *");
        SubscriptionRecurringJobSchedules.IctTimeZoneId.Should().Be("SE Asia Standard Time");

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
            SubscriptionRecurringJobSchedules.IctTimeZoneId);

        timeZone.Id.Should().Be("SE Asia Standard Time");
        timeZone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(7));
    }

    [Fact]
    public void NeighboringSubscriptionJobs_RetainTheirExistingSchedules()
    {
        SubscriptionLifecycleJob.ExpiryJobId.Should().Be("identity.subscription-expiry");
        SubscriptionRecurringJobSchedules.ExpiryCron.Should().Be("30 0 * * *");

        SubscriptionLifecycleJob.RevertJobId.Should().Be("identity.subscription-auto-revert");
        SubscriptionRecurringJobSchedules.RevertCron.Should().Be("* * * * *");

        SubscriptionLifecycleJob.MonthlyResetJobId.Should().Be("identity.subscription-monthly-reset");
        SubscriptionRecurringJobSchedules.MonthlyResetCron.Should().Be("1 0 1 * *");
    }
}
