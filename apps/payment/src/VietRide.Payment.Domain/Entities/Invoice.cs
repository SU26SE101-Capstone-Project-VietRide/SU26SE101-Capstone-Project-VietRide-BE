using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

public sealed class Invoice : BaseEntity<Guid>
{
    private Invoice() { }

    public string InvoiceNumber { get; private set; } = string.Empty;
    public Guid OperatorId { get; private set; }
    public Guid OperatorSubscriptionId { get; private set; }
    public Guid PaymentId { get; private set; }
    public Money Amount { get; private set; }
    public DateTimeOffset PeriodFrom { get; private set; }
    public DateTimeOffset PeriodTo { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTimeOffset? IssuedAt { get; private set; }
    public string? PdfUrl { get; private set; }
    public string? StorageObjectPath { get; private set; }
    public InvoicePdfGenerationStatus PdfGenerationStatus { get; private set; }
    public int PdfGenerationAttempts { get; private set; }
    public DateTimeOffset? PdfGenerationStartedAt { get; private set; }
    public DateTimeOffset? PdfGenerationNextRetryAt { get; private set; }
    public string? PdfGenerationLastError { get; private set; }
    public string? Metadata { get; private set; }

    public static Invoice CreateDraft(
        string invoiceNumber,
        Guid operatorId,
        Guid operatorSubscriptionId,
        Guid paymentId,
        Money amount,
        DateTimeOffset periodFrom,
        DateTimeOffset periodTo,
        string? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new ArgumentException("Invoice number is required.", nameof(invoiceNumber));
        if (operatorId == Guid.Empty || operatorSubscriptionId == Guid.Empty || paymentId == Guid.Empty)
            throw new ArgumentException("Invoice logical references are required.");
        if (periodTo <= periodFrom)
            throw new ArgumentException("Invoice period end must be after period start.", nameof(periodTo));

        return new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = invoiceNumber,
            OperatorId = operatorId,
            OperatorSubscriptionId = operatorSubscriptionId,
            PaymentId = paymentId,
            Amount = amount,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            Status = InvoiceStatus.DRAFT,
            PdfGenerationStatus = InvoicePdfGenerationStatus.PENDING,
            Metadata = metadata,
        };
    }
}
