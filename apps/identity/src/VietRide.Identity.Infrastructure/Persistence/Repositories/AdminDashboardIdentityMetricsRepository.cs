using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

internal sealed class AdminDashboardIdentityMetricsRepository
    : IAdminDashboardIdentityMetricsRepository
{
    private const string ActiveUsersMetric = "ACTIVE_USERS";
    private const string ApprovedOperatorMetric = "APPROVED_OPERATOR";
    private const string UserRoleMetric = "USER_ROLE";
    private const string OperatorStatusMetric = "OPERATOR_STATUS";

    private readonly IdentityDbContext _dbContext;

    public AdminDashboardIdentityMetricsRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminDashboardIdentityMetricsReadResult> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Database.SqlQuery<AdminDashboardIdentityMetricSqlRow>($"""
            WITH active_users AS (
                SELECT COUNT(*)::bigint AS count
                FROM vietride_identity.users
                WHERE deleted_at IS NULL
                  AND status NOT IN ('LOCKED', 'DELETED')
                  AND last_login_at >= {fromUtc}
                  AND last_login_at < {toUtcExclusive}
            ),
            approved_active_operators AS (
                SELECT id
                FROM vietride_identity.operators
                WHERE deleted_at IS NULL
                  AND registration_status = 'APPROVED'
                  AND is_active = TRUE
            ),
            user_role_counts AS (
                SELECT role::text AS key, COUNT(*)::bigint AS count
                FROM vietride_identity.users
                WHERE deleted_at IS NULL
                GROUP BY role
            ),
            operator_status_counts AS (
                SELECT registration_status::text AS key, COUNT(*)::bigint AS count
                FROM vietride_identity.operators
                WHERE deleted_at IS NULL
                GROUP BY registration_status
            )
            SELECT
                {ActiveUsersMetric}::text AS "MetricType",
                NULL::text AS "Key",
                NULL::uuid AS "GuidValue",
                active_users.count AS "Count"
            FROM active_users
            UNION ALL
            SELECT
                {ApprovedOperatorMetric}::text AS "MetricType",
                NULL::text AS "Key",
                approved_active_operators.id AS "GuidValue",
                0::bigint AS "Count"
            FROM approved_active_operators
            UNION ALL
            SELECT
                {UserRoleMetric}::text AS "MetricType",
                user_role_counts.key AS "Key",
                NULL::uuid AS "GuidValue",
                user_role_counts.count AS "Count"
            FROM user_role_counts
            UNION ALL
            SELECT
                {OperatorStatusMetric}::text AS "MetricType",
                operator_status_counts.key AS "Key",
                NULL::uuid AS "GuidValue",
                operator_status_counts.count AS "Count"
            FROM operator_status_counts
            """).ToListAsync(cancellationToken);

        var activeUserCount = rows.Single(row => row.MetricType == ActiveUsersMetric).Count;
        var approvedOperatorIds = rows
            .Where(row => row.MetricType == ApprovedOperatorMetric && row.GuidValue.HasValue)
            .Select(row => row.GuidValue!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var userRoleCounts = ToCounts(rows, UserRoleMetric);
        var operatorStatusCounts = ToCounts(rows, OperatorStatusMetric);

        return new AdminDashboardIdentityMetricsReadResult(
            activeUserCount,
            approvedOperatorIds,
            userRoleCounts,
            operatorStatusCounts);
    }

    private static IReadOnlyList<AdminDashboardIdentityMetricCountReadModel> ToCounts(
        IEnumerable<AdminDashboardIdentityMetricSqlRow> rows,
        string metricType)
        => rows
            .Where(row => row.MetricType == metricType && row.Key is not null)
            .Select(row => new AdminDashboardIdentityMetricCountReadModel(row.Key!, row.Count))
            .ToArray();

    private sealed class AdminDashboardIdentityMetricSqlRow
    {
        public string MetricType { get; set; } = string.Empty;
        public string? Key { get; set; }
        public Guid? GuidValue { get; set; }
        public long Count { get; set; }
    }
}
