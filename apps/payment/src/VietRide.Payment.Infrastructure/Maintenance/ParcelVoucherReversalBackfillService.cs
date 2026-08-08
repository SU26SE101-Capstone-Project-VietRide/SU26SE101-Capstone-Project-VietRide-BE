using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Features.Internal.Revenue.BackfillParcelVoucherReversals;
using VietRide.Payment.Application.Services;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Infrastructure.Maintenance;

public sealed class ParcelVoucherReversalBackfillService : IParcelVoucherReversalBackfillService
{
    private readonly PaymentDbContext _db;

    public ParcelVoucherReversalBackfillService(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task<BackfillParcelVoucherReversalsResult> ExecuteAsync(
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var refunds = await _db.OperatorLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.EntryType == OperatorLedgerEntryType.PARCEL_REFUND)
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var legacyUnclassifiedCount = await _db.OperatorLedgerEntries
            .AsNoTracking()
            .CountAsync(entry =>
                entry.EntryType == OperatorLedgerEntryType.ADJUSTMENT
                && entry.AdjustmentReason == OperatorLedgerAdjustmentReason.LEGACY_UNCLASSIFIED,
                cancellationToken)
            .ConfigureAwait(false);

        if (refunds.Length == 0)
        {
            return new BackfillParcelVoucherReversalsResult(
                0,
                0,
                0,
                legacyUnclassifiedCount,
                0,
                0);
        }

        var parcelIds = refunds.Select(entry => entry.ReferenceId).Distinct().ToArray();
        var credits = await _db.OperatorLedgerEntries
            .AsNoTracking()
            .Where(entry => parcelIds.Contains(entry.ReferenceId)
                && entry.ReferenceType == OperatorLedgerReferenceType.PARCEL
                && entry.EntryType == OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT)
            .GroupBy(entry => entry.ReferenceId)
            .Select(group => new { ParcelId = group.Key, Amount = group.Sum(entry => entry.Amount) })
            .ToDictionaryAsync(item => item.ParcelId, item => item.Amount, cancellationToken)
            .ConfigureAwait(false);
        var existingSourceRows = await _db.OperatorLedgerEntries
            .AsNoTracking()
            .Where(entry => parcelIds.Contains(entry.ReferenceId)
                && entry.EntryType == OperatorLedgerEntryType.ADJUSTMENT
                && entry.AdjustmentReason == OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL)
            .Select(entry => entry.SourceEventId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingSources = existingSourceRows.ToHashSet();
        var existingParcelIds = await _db.OperatorLedgerEntries
            .AsNoTracking()
            .Where(entry => parcelIds.Contains(entry.ReferenceId)
                && entry.EntryType == OperatorLedgerEntryType.ADJUSTMENT
                && entry.AdjustmentReason == OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL)
            .Select(entry => entry.ReferenceId)
            .Distinct()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var reversedParcelIds = existingParcelIds.ToHashSet();

        var candidates = new List<(OperatorLedgerEntry Refund, Guid SourceId, long Amount)>();
        var scheduledParcelIds = new HashSet<Guid>();
        var skippedExistingCount = 0;
        foreach (var refund in refunds)
        {
            if (!credits.TryGetValue(refund.ReferenceId, out var voucherAmount) || voucherAmount <= 0)
                continue;

            var sourceId = RevenueLedgerWriter.CreateParcelVoucherAdjustmentSourceId(
                refund.SourceEventId,
                refund.ReferenceId);
            if (existingSources.Contains(sourceId)
                || reversedParcelIds.Contains(refund.ReferenceId)
                || !scheduledParcelIds.Add(refund.ReferenceId))
            {
                skippedExistingCount++;
                continue;
            }

            candidates.Add((refund, sourceId, checked(-voucherAmount)));
        }

        var totalAdjustmentVnd = candidates.Aggregate(
            0L,
            (total, candidate) => checked(total + candidate.Amount));
        if (!dryRun)
        {
            foreach (var candidate in candidates)
            {
                _db.OperatorLedgerEntries.Add(OperatorLedgerEntry.Create(
                    candidate.Refund.OperatorId,
                    candidate.Refund.TripId,
                    OperatorLedgerEntryType.ADJUSTMENT,
                    candidate.Amount,
                    OperatorLedgerReferenceType.PARCEL,
                    candidate.Refund.ReferenceId,
                    candidate.SourceId,
                    "reverse-vietride-funded-voucher",
                    adjustmentReason: OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL));
            }

            if (candidates.Count > 0)
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new BackfillParcelVoucherReversalsResult(
            refunds.Length,
            candidates.Count,
            skippedExistingCount,
            legacyUnclassifiedCount,
            totalAdjustmentVnd,
            dryRun ? 0 : candidates.Count);
    }
}
