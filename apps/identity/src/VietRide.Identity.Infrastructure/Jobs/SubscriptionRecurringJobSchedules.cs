namespace VietRide.Identity.Infrastructure.Jobs;

public static class SubscriptionRecurringJobSchedules
{
    // 00:30 Asia/Ho_Chi_Minh (previous UTC date is expected).
    public const string ExpiryCron = "30 17 * * *";
    // 09:00 Asia/Ho_Chi_Minh.
    public const string WarningCron = "0 2 * * *";
    public const string RevertCron = "* * * * *";
    // 00:01 Asia/Ho_Chi_Minh. Running daily in UTC is intentional; the job's month boundary makes later runs no-ops.
    public const string MonthlyResetCron = "1 17 * * *";
}
