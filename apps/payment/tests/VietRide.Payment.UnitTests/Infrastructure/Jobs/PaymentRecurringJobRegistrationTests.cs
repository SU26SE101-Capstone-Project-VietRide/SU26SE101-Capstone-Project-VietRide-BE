using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using VietRide.Payment.Infrastructure.Jobs;

namespace VietRide.Payment.UnitTests.Infrastructure.Jobs;

public sealed class PaymentRecurringJobRegistrationTests
{
    [Fact]
    public void Register_RegistersEveryPaymentJobWithUtcTimeZone()
    {
        var manager = new RecordingRecurringJobManager();

        PaymentRecurringJobRegistration.Register(manager);
        PaymentRecurringJobRegistration.RegisterInvoiceJobs(manager, "15 * * * *");

        manager.Registrations.Should().HaveCount(11);
        manager.Registrations.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        manager.Registrations.Should().OnlyContain(item => item.Options.TimeZone == TimeZoneInfo.Utc);
        manager.Registrations.Should().ContainSingle(item =>
            item.Id == TripSettlementEligibilityFlagJob.RecurringJobId
            && item.Cron == "0 19 * * *");
        manager.Registrations.Should().ContainSingle(item =>
            item.Id == TripSettlementWeeklyAutoSettleJob.RecurringJobId
            && item.Cron == "0 2 * * 1");
        manager.Registrations.Should().ContainSingle(item =>
            item.Id == InvoicePdfReconciliationJob.RecurringJobId
            && item.Cron == "15 * * * *");
    }

    private sealed class RecordingRecurringJobManager : IRecurringJobManager
    {
        public List<Registration> Registrations { get; } = [];

        public void AddOrUpdate(
            string recurringJobId,
            Job job,
            string cronExpression,
            RecurringJobOptions options) =>
            Registrations.Add(new Registration(recurringJobId, job, cronExpression, options));

        public void RemoveIfExists(string recurringJobId)
        {
        }

        public void Trigger(string recurringJobId)
        {
        }
    }

    private sealed record Registration(
        string Id,
        Job Job,
        string Cron,
        RecurringJobOptions Options);
}
