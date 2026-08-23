using Hangfire;

namespace VietRide.Payment.Infrastructure.Jobs;

public static class PaymentRecurringJobRegistration
{
    public static void Register(IRecurringJobManager recurringJobs)
    {
        ArgumentNullException.ThrowIfNull(recurringJobs);

        recurringJobs.AddOrUpdate<TopUpExpiredJob>(
            TopUpExpiredJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            Cron.Minutely(),
            UtcOptions());
        recurringJobs.AddOrUpdate<RefundFailureRetryJob>(
            RefundFailureRetryJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            "*/10 * * * *",
            UtcOptions());
        recurringJobs.AddOrUpdate<PaymentExpiredJob>(
            PaymentExpiredJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            Cron.Minutely(),
            UtcOptions());
        recurringJobs.AddOrUpdate<Day38PaymentContextBackfillJob>(
            Day38PaymentContextBackfillJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            "*/5 * * * *",
            UtcOptions());
        recurringJobs.AddOrUpdate<Day38RevenueLedgerBackfillJob>(
            Day38RevenueLedgerBackfillJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            "*/10 * * * *",
            UtcOptions());
        recurringJobs.AddOrUpdate<TripSettlementEligibilityFlagJob>(
            TripSettlementEligibilityFlagJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            // 02:00 Asia/Ho_Chi_Minh.
            "0 19 * * *",
            UtcOptions());
        recurringJobs.AddOrUpdate<TripSettlementWeeklyAutoSettleJob>(
            TripSettlementWeeklyAutoSettleJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            // Monday 09:00 Asia/Ho_Chi_Minh.
            "0 2 * * 1",
            UtcOptions());
        recurringJobs.AddOrUpdate<TripSettlementStuckAlertJob>(
            TripSettlementStuckAlertJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            Cron.Hourly(),
            UtcOptions());
        recurringJobs.AddOrUpdate<FinancialProjectionBackfillJob>(
            FinancialProjectionBackfillJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            "*/5 * * * *",
            UtcOptions());
        recurringJobs.AddOrUpdate<PaymentBusinessCodeBackfillJob>(
            PaymentBusinessCodeBackfillJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            "*/5 * * * *",
            UtcOptions());
    }

    public static void RegisterInvoiceJobs(
        IRecurringJobManager recurringJobs,
        string reconciliationCron)
    {
        ArgumentNullException.ThrowIfNull(recurringJobs);
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciliationCron);

        recurringJobs.AddOrUpdate<InvoicePdfReconciliationJob>(
            InvoicePdfReconciliationJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            reconciliationCron,
            UtcOptions());
        recurringJobs.AddOrUpdate<Day38InvoiceBackfillJob>(
            Day38InvoiceBackfillJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            "*/10 * * * *",
            UtcOptions());
    }

    private static RecurringJobOptions UtcOptions() => new() { TimeZone = TimeZoneInfo.Utc };
}
