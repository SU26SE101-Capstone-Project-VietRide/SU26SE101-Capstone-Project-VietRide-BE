using Microsoft.EntityFrameworkCore;
using Npgsql;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class RevenueAnalyticsRepository : IRevenueAnalyticsRepository
{
    private readonly PaymentDbContext dbContext;

    public RevenueAnalyticsRepository(PaymentDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AdminRevenueMonthReadModel>> GetAdminMonthlyRevenueAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
        => await dbContext.Database.SqlQueryRaw<AdminRevenueMonthReadModel>(
            """
            WITH subscription_monthly AS (
                SELECT date_trunc('month', succeeded_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date AS month,
                       SUM(amount)::bigint AS platform_revenue_vnd
                FROM vietride_payment.payments
                WHERE reference_type = 'SUBSCRIPTION'
                  AND status = 'SUCCEEDED'
                  AND succeeded_at >= @from_utc
                  AND succeeded_at < @to_utc
                GROUP BY month
            ),
            payout_monthly AS (
                SELECT date_trunc('month', settled_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date AS month,
                       SUM(net_amount)::bigint AS paid_to_operators_vnd
                FROM vietride_payment.operator_trip_settlements
                WHERE status = 'SETTLED'
                  AND settled_at >= @from_utc
                  AND settled_at < @to_utc
                GROUP BY month
            )
            SELECT COALESCE(subscription.month, payout.month) AS "Month",
                   COALESCE(subscription.platform_revenue_vnd, 0)::bigint AS "PlatformRevenueVnd",
                   COALESCE(payout.paid_to_operators_vnd, 0)::bigint AS "PaidToOperatorsVnd"
            FROM subscription_monthly AS subscription
            FULL OUTER JOIN payout_monthly AS payout ON payout.month = subscription.month
            ORDER BY "Month";
            """,
            new NpgsqlParameter("from_utc", fromUtc.ToUniversalTime()),
            new NpgsqlParameter("to_utc", toUtc.ToUniversalTime()))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TopOperatorPayoutReadModel>> GetTopOperatorPayoutsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int top,
        CancellationToken cancellationToken = default)
    {
        if (top is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(top), top, "top must be between 1 and 20.");
        }

        return await dbContext.Database.SqlQueryRaw<TopOperatorPayoutReadModel>(
            """
            SELECT operator_id AS "OperatorId",
                   SUM(net_amount)::bigint AS "RevenueVnd"
            FROM vietride_payment.operator_trip_settlements
            WHERE status = 'SETTLED'
              AND settled_at >= @from_utc
              AND settled_at < @to_utc
            GROUP BY operator_id
            ORDER BY "RevenueVnd" DESC, "OperatorId"
            LIMIT @top;
            """,
            new NpgsqlParameter("from_utc", fromUtc.ToUniversalTime()),
            new NpgsqlParameter("to_utc", toUtc.ToUniversalTime()),
            new NpgsqlParameter("top", top))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OperatorRevenueLedgerReadModel>> GetOperatorRevenueLedgerAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("Operator id must be non-empty.", nameof(operatorId));
        }

        return await dbContext.Database.SqlQueryRaw<OperatorRevenueLedgerReadModel>(
            """
            WITH eligible AS (
                SELECT date_trunc('month', created_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date AS month,
                       trip_id,
                       reference_id,
                       CASE WHEN reference_type = 'BOOKING'
                                  AND entry_type IN ('BOOKING_REVENUE', 'BOOKING_REFUND', 'VOUCHER_VIETRIDE_FUNDED_CREDIT')
                                   OR reference_type = 'BOOKING'
                                      AND entry_type = 'ADJUSTMENT'
                                      AND amount < 0
                                      AND note = 'reverse-vietride-funded-voucher'
                            THEN amount ELSE 0 END AS ticket_revenue_vnd,
                       CASE WHEN reference_type = 'PARCEL'
                                  AND entry_type IN ('PARCEL_REVENUE', 'PARCEL_REFUND', 'VOUCHER_VIETRIDE_FUNDED_CREDIT')
                                   OR reference_type = 'PARCEL'
                                      AND entry_type = 'ADJUSTMENT'
                                      AND amount < 0
                                      AND note = 'reverse-vietride-funded-voucher'
                            THEN amount ELSE 0 END AS parcel_revenue_vnd,
                       CASE WHEN reference_type = 'BOOKING'
                                  AND (entry_type IN ('BOOKING_REVENUE', 'BOOKING_REFUND', 'VOUCHER_VIETRIDE_FUNDED_CREDIT')
                                       OR entry_type = 'ADJUSTMENT'
                                          AND amount < 0
                                          AND note = 'reverse-vietride-funded-voucher')
                            THEN reference_id END AS booking_reference_id,
                       CASE WHEN reference_type = 'PARCEL'
                                  AND (entry_type IN ('PARCEL_REVENUE', 'PARCEL_REFUND', 'VOUCHER_VIETRIDE_FUNDED_CREDIT')
                                       OR entry_type = 'ADJUSTMENT'
                                          AND amount < 0
                                          AND note = 'reverse-vietride-funded-voucher')
                            THEN reference_id END AS parcel_reference_id
                FROM vietride_payment.operator_ledger_entries
                WHERE operator_id = @operator_id
                  AND created_at >= @from_utc
                  AND created_at < @to_utc
                  AND (
                      reference_type = 'BOOKING'
                      AND (entry_type IN ('BOOKING_REVENUE', 'BOOKING_REFUND', 'VOUCHER_VIETRIDE_FUNDED_CREDIT')
                           OR entry_type = 'ADJUSTMENT'
                              AND amount < 0
                              AND note = 'reverse-vietride-funded-voucher')
                      OR reference_type = 'PARCEL'
                      AND (entry_type IN ('PARCEL_REVENUE', 'PARCEL_REFUND', 'VOUCHER_VIETRIDE_FUNDED_CREDIT')
                           OR entry_type = 'ADJUSTMENT'
                              AND amount < 0
                              AND note = 'reverse-vietride-funded-voucher')
                  )
            )
            SELECT month AS "Month",
                   trip_id AS "TripId",
                   SUM(ticket_revenue_vnd)::bigint AS "TicketRevenueVnd",
                   SUM(parcel_revenue_vnd)::bigint AS "ParcelRevenueVnd",
                   COUNT(DISTINCT booking_reference_id)::integer AS "BookingCount",
                   COUNT(DISTINCT parcel_reference_id)::integer AS "ParcelCount"
            FROM eligible
            GROUP BY month, trip_id
            ORDER BY month, trip_id NULLS LAST;
            """,
            new NpgsqlParameter("operator_id", operatorId),
            new NpgsqlParameter("from_utc", fromUtc.ToUniversalTime()),
            new NpgsqlParameter("to_utc", toUtc.ToUniversalTime()))
            .ToListAsync(cancellationToken);
    }
}
