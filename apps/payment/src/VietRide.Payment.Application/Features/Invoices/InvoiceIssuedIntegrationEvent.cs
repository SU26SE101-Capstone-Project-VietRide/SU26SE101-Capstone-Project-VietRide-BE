using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Features.Invoices;

public sealed class InvoiceIssuedIntegrationEvent(
    Guid invoiceId,
    string invoiceNumber,
    Guid operatorId,
    long amount,
    string invoiceWebUrl,
    string downloadApiUrl) : IntegrationEventBase
{
    public override string EventType => "payment.invoice.issued";

    public Guid InvoiceId { get; } = invoiceId;
    public string InvoiceNumber { get; } = invoiceNumber;
    public Guid OperatorId { get; } = operatorId;
    public long Amount { get; } = amount;
    public string InvoiceWebUrl { get; } = invoiceWebUrl;
    public string DownloadApiUrl { get; } = downloadApiUrl;
}
