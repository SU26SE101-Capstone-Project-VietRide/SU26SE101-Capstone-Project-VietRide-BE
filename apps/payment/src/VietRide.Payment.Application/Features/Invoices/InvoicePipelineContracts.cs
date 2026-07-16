using System.Text.Json;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Models;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Features.Invoices;

public sealed record InvoiceMetadataV1(
    int Version,
    string PlanName,
    string BillingPeriod,
    SubscriptionBuyerSnapshotV1 BuyerSnapshot);

public static class InvoiceMetadataCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(InvoiceMetadataV1 metadata)
        => JsonSerializer.Serialize(metadata, JsonOptions);

    public static InvoiceMetadataV1 Deserialize(string json)
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<InvoiceMetadataV1>(json, JsonOptions);
            if (metadata is null
                || metadata.Version != 1
                || string.IsNullOrWhiteSpace(metadata.PlanName)
                || metadata.BillingPeriod is not ("MONTHLY" or "YEARLY")
                || metadata.BuyerSnapshot is null)
            {
                throw new InvalidOperationException("Stored invoice metadata is invalid.");
            }

            return metadata;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Stored invoice metadata is malformed.", exception);
        }
    }
}

public interface IInvoiceJobScheduler
{
    void EnqueuePdfGeneration(Guid invoiceId);
}

public interface IInvoiceLifecycleService
{
    Task<RetryInvoiceResult> RetryAsync(Guid invoiceId, CancellationToken cancellationToken);

    Task<InvoiceDownloadUrl> CreateDownloadAsync(
        Guid invoiceId,
        Guid operatorId,
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed class InvoicePdfUnavailableException : Exception, ICodedHttpException
{
    public InvoicePdfUnavailableException()
        : base("Invoice PDF is not available.")
    {
    }

    public int StatusCode => 500;
    public string ErrorCode => "INVOICE_PDF_GENERATION_FAILED";
}
