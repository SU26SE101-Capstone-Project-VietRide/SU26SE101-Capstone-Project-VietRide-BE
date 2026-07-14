using System.Text.Json.Serialization;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Features.Invoices;

public sealed record SubscriptionPaymentSucceededInvoiceEvent(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("occurredAt")] DateTime OccurredAt,
    [property: JsonPropertyName("paymentId")] Guid PaymentId,
    [property: JsonPropertyName("upgradeAttemptId")] Guid UpgradeAttemptId,
    [property: JsonPropertyName("operatorId")] Guid OperatorId,
    [property: JsonPropertyName("operatorSubscriptionId")] Guid OperatorSubscriptionId,
    [property: JsonPropertyName("amount")] long Amount,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("planName")] string PlanName,
    [property: JsonPropertyName("billingPeriod")] string BillingPeriod,
    [property: JsonPropertyName("periodFrom")] DateTimeOffset PeriodFrom,
    [property: JsonPropertyName("periodTo")] DateTimeOffset PeriodTo,
    [property: JsonPropertyName("buyerSnapshot")] SubscriptionBuyerSnapshotV1 BuyerSnapshot)
    : IIntegrationEvent
{
    public const string EventTypeValue = "payment.subscription.payment_succeeded";

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventTypeValue;
}

public sealed class SubscriptionPaymentSucceededInvoiceHandler
    : IIntegrationEventHandler<SubscriptionPaymentSucceededInvoiceEvent>
{
    private const string ConsumerName = "payment.subscription-invoice";
    private readonly IInvoiceRepository _invoices;
    private readonly IInvoiceNumberCounterRepository _counters;
    private readonly IProcessedIntegrationEventRepository _processed;
    private readonly IInvoiceJobScheduler _jobs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public SubscriptionPaymentSucceededInvoiceHandler(
        IInvoiceRepository invoices,
        IInvoiceNumberCounterRepository counters,
        IProcessedIntegrationEventRepository processed,
        IInvoiceJobScheduler jobs,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _invoices = invoices;
        _counters = counters;
        _processed = processed;
        _jobs = jobs;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task HandleAsync(
        SubscriptionPaymentSucceededInvoiceEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        Guid? createdInvoiceId = null;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await _processed.ExistsAsync(ConsumerName, integrationEvent.EventId, cancellationToken))
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return;
            }

            var existing = await _invoices.FindByPaymentIdAsync(
                integrationEvent.PaymentId,
                cancellationToken);
            if (existing is null)
            {
                var periodKey = integrationEvent.OccurredAt.ToUniversalTime().ToString("yyyyMM");
                var sequence = await _counters.NextAsync(periodKey, cancellationToken);
                var invoiceNumber = $"VR-INV-{periodKey}-{sequence:000000}";
                var metadata = new InvoiceMetadataV1(
                    1,
                    integrationEvent.PlanName,
                    integrationEvent.BillingPeriod,
                    integrationEvent.BuyerSnapshot);
                var invoice = Invoice.CreateDraft(
                    invoiceNumber,
                    integrationEvent.OperatorId,
                    integrationEvent.OperatorSubscriptionId,
                    integrationEvent.PaymentId,
                    Money.FromRaw(integrationEvent.Amount),
                    integrationEvent.PeriodFrom,
                    integrationEvent.PeriodTo,
                    InvoiceMetadataCodec.Serialize(metadata));
                await _invoices.AddAsync(invoice, cancellationToken);
                createdInvoiceId = invoice.Id;
            }

            await _processed.AddAsync(
                ProcessedIntegrationEvent.Create(ConsumerName, integrationEvent.EventId, _clock.UtcNow),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        if (createdInvoiceId.HasValue)
            _jobs.EnqueuePdfGeneration(createdInvoiceId.Value);
    }
}
