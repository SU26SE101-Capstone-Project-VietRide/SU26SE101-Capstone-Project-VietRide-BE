namespace VietRide.Identity.Infrastructure.Jobs;

public static class SubscriptionRecurringJobSchedules
{
    public const string IctTimeZoneId = "SE Asia Standard Time";
    public const string ExpiryCron = "30 0 * * *";
    public const string WarningCron = "0 9 * * *";
    public const string RevertCron = "* * * * *";
    public const string MonthlyResetCron = "1 0 1 * *";
}
