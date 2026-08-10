using VietRide.Shared.Kernel.Time;

namespace VietRide.Payment.Application.Features.Invoices;

public static class InvoiceNumberPeriod
{
    public static string FromInstant(DateTimeOffset instant) =>
        BusinessTime.ToLocalDate(instant).ToString("yyyyMM");
}
