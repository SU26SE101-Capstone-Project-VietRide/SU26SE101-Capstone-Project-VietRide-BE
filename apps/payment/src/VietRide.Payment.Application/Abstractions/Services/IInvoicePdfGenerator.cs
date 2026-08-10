namespace VietRide.Payment.Application.Abstractions.Services;

public interface IInvoicePdfGenerator
{
    Task<byte[]> GenerateAsync(InvoicePdfDocument document, CancellationToken cancellationToken);
}

public sealed record InvoicePdfDocument(
    string InvoiceNumber,
    DateTimeOffset IssuedAt,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    string PlanName,
    string BillingPeriod,
    long AmountVnd,
    InvoicePdfBuyer Buyer);

public sealed record InvoicePdfBuyer(
    string Name,
    string BusinessRegistrationNumber,
    string TaxCode,
    string ContactEmail,
    string ContactPhone,
    string? AddressStreet,
    string? AddressWard,
    string? AddressProvince);
