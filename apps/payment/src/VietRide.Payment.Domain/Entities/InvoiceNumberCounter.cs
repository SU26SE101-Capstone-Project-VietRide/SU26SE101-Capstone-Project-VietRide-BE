namespace VietRide.Payment.Domain.Entities;

public sealed class InvoiceNumberCounter
{
    private InvoiceNumberCounter() { }

    public string PeriodKey { get; private set; } = string.Empty;
    public long LastValue { get; private set; }
}
