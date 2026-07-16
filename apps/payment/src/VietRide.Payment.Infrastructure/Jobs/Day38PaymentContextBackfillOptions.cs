namespace VietRide.Payment.Infrastructure.Jobs;

public sealed class Day38PaymentContextBackfillOptions
{
    public const string SectionName = "PaymentContextBackfill";

    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; } = true;
    public int MaxBatchSize { get; set; } = 100;
}
