using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Infrastructure.Jobs;

public sealed class Day38PaymentContextBackfillJob
{
    public const string RecurringJobId = "payment.day38-context-backfill";
    public const string BookingHttpClientName = "day38-payment-context-booking";
    public const string ParcelHttpClientName = "day38-payment-context-parcel";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PaymentDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Day38PaymentContextBackfillOptions _options;
    private readonly ILogger<Day38PaymentContextBackfillJob> _logger;

    public Day38PaymentContextBackfillJob(
        PaymentDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<Day38PaymentContextBackfillOptions> options,
        ILogger<Day38PaymentContextBackfillJob> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return;

        var maxBatchSize = Math.Clamp(_options.MaxBatchSize, 1, 1_000);
        var payments = await _db.Payments
            .Where(payment => payment.Context == "{}"
                && (payment.Status == PaymentStatus.PENDING_REDIRECT
                    || payment.Status == PaymentStatus.SUCCEEDED))
            .OrderBy(payment => payment.CreatedAt)
            .Take(maxBatchSize)
            .ToListAsync(cancellationToken);

        var hydrated = 0;
        var quarantined = 0;
        var errors = 0;

        foreach (var payment in payments)
        {
            try
            {
                var snapshot = await GetSnapshotAsync(
                    payment.ReferenceType.ToString(),
                    payment.ReferenceId,
                    cancellationToken);

                if (!snapshot.CanBackfill)
                {
                    quarantined++;
                    if (!_options.DryRun && !payment.ContextReconciliationRequired)
                        payment.MarkContextReconciliationRequired();

                    _logger.LogWarning(
                        "Payment context backfill quarantined {PaymentId}: {Reason}.",
                        payment.Id,
                        snapshot.QuarantineReason ?? "UNSPECIFIED");
                    continue;
                }

                var context = new PaymentContextV1(snapshot.Version, snapshot.Allocations);
                var json = PaymentContextCodec.ValidateAndSerialize(
                    context,
                    payment.ReferenceType.ToString(),
                    payment.ReferenceId,
                    payment.Amount.Amount);

                hydrated++;
                if (!_options.DryRun)
                    payment.AttachContext(json);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors++;
                if (!_options.DryRun && !payment.ContextReconciliationRequired)
                    payment.MarkContextReconciliationRequired();

                _logger.LogError(
                    exception,
                    "Payment context backfill failed for payment {PaymentId} ({ReferenceType}/{ReferenceId}).",
                    payment.Id,
                    payment.ReferenceType,
                    payment.ReferenceId);
            }
        }

        if (!_options.DryRun)
            await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Day 38 payment context backfill completed. DryRun={DryRun}, Scanned={Scanned}, Hydrated={Hydrated}, Quarantined={Quarantined}, Errors={Errors}.",
            _options.DryRun,
            payments.Count,
            hydrated,
            quarantined,
            errors);
    }

    private async Task<PaymentContextSnapshotResponse> GetSnapshotAsync(
        string referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
    {
        var isBooking = referenceType is "BOOKING" or "BOOKING_GROUP";
        var client = _httpClientFactory.CreateClient(
            isBooking ? BookingHttpClientName : ParcelHttpClientName);
        var servicePath = isBooking ? "bookings" : "parcels";
        var path = $"/internal/v1/{servicePath}/payment-context/{referenceType}/{referenceId:D}";

        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
        var data = payload.TryGetProperty("data", out var envelopeData) ? envelopeData : payload;
        return data.Deserialize<PaymentContextSnapshotResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Payment context owner returned an empty snapshot.");
    }

    private sealed record PaymentContextSnapshotResponse(
        int Version,
        bool CanBackfill,
        string? QuarantineReason,
        IReadOnlyList<PaymentAllocationV1> Allocations);
}
