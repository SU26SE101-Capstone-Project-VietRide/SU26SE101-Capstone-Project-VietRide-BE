using Microsoft.EntityFrameworkCore;
using Npgsql;
using VietRide.Payment.Application.Features.Management;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal static class CanonicalTripFinancialProjectionQuery
{
    private const string ProjectionSql = """
        SELECT operator_id AS "OperatorId",
               trip_id AS "TripId",
               (COALESCE(SUM(amount) FILTER (
                    WHERE entry_type IN ('BOOKING_REVENUE', 'PARCEL_REVENUE', 'VOUCHER_VIETRIDE_FUNDED_CREDIT')), 0)
                + COALESCE(SUM(operator_funded_voucher_amount) FILTER (
                    WHERE entry_type = 'VOUCHER_OPERATOR_FUNDED_AUDIT'), 0))::bigint AS "GrossSalesAmount",
               COALESCE(SUM(amount) FILTER (
                    WHERE entry_type IN ('BOOKING_REVENUE', 'PARCEL_REVENUE')), 0)::bigint AS "PassengerPaidAmount",
               COALESCE(SUM(amount) FILTER (
                    WHERE entry_type = 'VOUCHER_VIETRIDE_FUNDED_CREDIT'), 0)::bigint AS "VietRideFundedAmount",
               COALESCE(SUM(operator_funded_voucher_amount) FILTER (
                    WHERE entry_type = 'VOUCHER_OPERATOR_FUNDED_AUDIT'), 0)::bigint AS "OperatorFundedDiscountAmount",
               -COALESCE(SUM(amount) FILTER (
                    WHERE entry_type IN ('BOOKING_REFUND', 'PARCEL_REFUND')), 0)::bigint AS "RefundAmount",
               COALESCE(SUM(amount) FILTER (
                    WHERE entry_type = 'ADJUSTMENT'
                      AND adjustment_reason = 'VIETRIDE_FUNDED_VOUCHER_REVERSAL'), 0)::bigint AS "RecognizedAdjustmentAmount",
               COALESCE(SUM(amount) FILTER (
                    WHERE entry_type <> 'VOUCHER_OPERATOR_FUNDED_AUDIT'), 0)::bigint AS "NetEntitlementAmount",
               BOOL_AND(reference_code IS NOT NULL
                    AND occurred_at IS NOT NULL
                    AND (entry_type <> 'VOUCHER_OPERATOR_FUNDED_AUDIT'
                         OR operator_funded_voucher_amount IS NOT NULL)) AS "MetadataComplete"
        FROM vietride_payment.operator_ledger_entries
        WHERE operator_id = @operator_id
          AND trip_id IS NOT NULL
          AND ((reference_type = 'BOOKING'
                AND (entry_type IN ('BOOKING_REVENUE', 'BOOKING_REFUND', 'VOUCHER_VIETRIDE_FUNDED_CREDIT')
                     OR entry_type = 'ADJUSTMENT'
                        AND adjustment_reason = 'VIETRIDE_FUNDED_VOUCHER_REVERSAL'))
               OR (reference_type = 'PARCEL'
                   AND (entry_type IN ('PARCEL_REVENUE', 'PARCEL_REFUND', 'VOUCHER_VIETRIDE_FUNDED_CREDIT')
                        OR entry_type = 'ADJUSTMENT'
                           AND adjustment_reason = 'VIETRIDE_FUNDED_VOUCHER_REVERSAL'))
               OR entry_type = 'VOUCHER_OPERATOR_FUNDED_AUDIT')
        GROUP BY operator_id, trip_id
        """;

    public static IQueryable<TripFinancialProjection> ForOperator(
        PaymentDbContext db,
        Guid operatorId)
        => db.Database.SqlQueryRaw<TripFinancialProjection>(
            ProjectionSql,
            new NpgsqlParameter("operator_id", operatorId));
}
