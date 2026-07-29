using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Infrastructure.Jobs;

public sealed class FinancialProjectionBackfillJob
{
    public const string RecurringJobId = "payment.financial-projection-backfill";
    public const int BatchSize = 100;

    private readonly PaymentDbContext _db;
    private readonly IIdentityFinancialProjectionClient _identity;
    private readonly ILogger<FinancialProjectionBackfillJob> _logger;

    public FinancialProjectionBackfillJob(
        PaymentDbContext db,
        IIdentityFinancialProjectionClient identity,
        ILogger<FinancialProjectionBackfillJob> logger)
    {
        _db = db;
        _identity = identity;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var settlements = await _db.OperatorTripSettlements
            .AsNoTracking()
            .Where(item => !item.OperatorSnapshotResolved
                || (item.SettledByUserId.HasValue && !item.SettledBySnapshotResolved))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(BatchSize)
            .ToArrayAsync(cancellationToken);
        var platformTransactions = await _db.PlatformWalletTransactions
            .AsNoTracking()
            .Where(item => item.ActorType == FinancialActorType.USER
                && item.ActorUserId.HasValue
                && !item.ActorSnapshotResolved)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(BatchSize)
            .ToArrayAsync(cancellationToken);
        if (settlements.Length == 0 && platformTransactions.Length == 0)
            return;

        var operatorIds = settlements
            .Where(item => !item.OperatorSnapshotResolved)
            .Select(item => item.OperatorId)
            .Distinct()
            .ToArray();
        var userIds = settlements
            .Where(item => item.SettledByUserId.HasValue && !item.SettledBySnapshotResolved)
            .Select(item => item.SettledByUserId!.Value)
            .Distinct()
            .ToArray();
        var platformUserIds = platformTransactions
            .Select(item => item.ActorUserId!.Value)
            .Distinct()
            .ToArray();
        var operatorTask = operatorIds.Length == 0
            ? Task.FromResult<IReadOnlyList<IdentityFinancialOperator>>([])
            : _identity.GetOperatorsAsync(operatorIds, cancellationToken);
        var userTask = userIds.Length == 0
            ? Task.FromResult<IReadOnlyList<IdentityFinancialUser>>([])
            : _identity.GetUsersAsync(userIds, cancellationToken);
        var platformUserTask = platformUserIds.Length == 0
            ? Task.FromResult<IReadOnlyList<IdentityFinancialUser>>([])
            : _identity.GetUsersAsync(platformUserIds, cancellationToken);
        await Task.WhenAll(operatorTask, userTask, platformUserTask);
        var operators = (await operatorTask).ToDictionary(item => item.OperatorId);
        var users = (await userTask).ToDictionary(item => item.UserId);
        var platformUsers = (await platformUserTask).ToDictionary(item => item.UserId);

        var operatorUpdates = settlements
            .Where(item => !item.OperatorSnapshotResolved)
            .Select(settlement =>
            {
                operators.TryGetValue(settlement.OperatorId, out var item);
                return new
                {
                    settlement.Id,
                    Name = item?.Name,
                    item?.LogoUrl,
                    item?.ContactPhone,
                };
            })
            .ToArray();
        var settledByUpdates = settlements
            .Where(item => item.SettledByUserId.HasValue && !item.SettledBySnapshotResolved)
            .Select(settlement =>
            {
                users.TryGetValue(settlement.SettledByUserId!.Value, out var item);
                var active = item is not null && !item.Deleted && !string.IsNullOrWhiteSpace(item.Email);
                return new
                {
                    settlement.Id,
                    UserId = settlement.SettledByUserId.Value,
                    DisplayName = active ? item!.DisplayName : null,
                    Email = active ? item!.Email : null,
                    Role = active ? item!.Role : null,
                };
            })
            .ToArray();
        var platformUpdates = platformTransactions
            .Select(transaction =>
            {
                platformUsers.TryGetValue(transaction.ActorUserId!.Value, out var item);
                var active = item is not null && !item.Deleted && !string.IsNullOrWhiteSpace(item.Email);
                return new
                {
                    transaction.Id,
                    UserId = transaction.ActorUserId.Value,
                    DisplayName = active ? item!.DisplayName : null,
                    Email = active ? item!.Email : null,
                    Role = active ? item!.Role : null,
                };
            })
            .ToArray();

        await ApplyOperatorSnapshotsAsync(
            operatorUpdates.Select(item => item.Id).ToArray(),
            operatorUpdates.Select(item => item.Name).ToArray(),
            operatorUpdates.Select(item => item.LogoUrl).ToArray(),
            operatorUpdates.Select(item => item.ContactPhone).ToArray(),
            cancellationToken);
        await ApplySettledBySnapshotsAsync(
            settledByUpdates.Select(item => item.Id).ToArray(),
            settledByUpdates.Select(item => item.UserId).ToArray(),
            settledByUpdates.Select(item => item.DisplayName).ToArray(),
            settledByUpdates.Select(item => item.Email).ToArray(),
            settledByUpdates.Select(item => item.Role).ToArray(),
            cancellationToken);
        await ApplyPlatformActorSnapshotsAsync(
            platformUpdates.Select(item => item.Id).ToArray(),
            platformUpdates.Select(item => item.UserId).ToArray(),
            platformUpdates.Select(item => item.DisplayName).ToArray(),
            platformUpdates.Select(item => item.Email).ToArray(),
            platformUpdates.Select(item => item.Role).ToArray(),
            cancellationToken);
        _logger.LogInformation(
            "Financial projection backfill resolved {SettlementCount} settlement rows and {TransactionCount} platform transactions.",
            settlements.Length,
            platformTransactions.Length);
    }

    private Task<int> ApplyOperatorSnapshotsAsync(
        Guid[] settlementIds,
        string?[] names,
        string?[] logoUrls,
        string?[] contactPhones,
        CancellationToken cancellationToken)
    {
        if (settlementIds.Length == 0)
            return Task.FromResult(0);

        return _db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH snapshot_updates (settlement_id, name, logo_url, contact_phone) AS (
                SELECT *
                FROM unnest(
                    {settlementIds}::uuid[],
                    {names}::text[],
                    {logoUrls}::text[],
                    {contactPhones}::text[])
            )
            UPDATE vietride_payment.operator_trip_settlements AS settlement
            SET operator_name = snapshot.name,
                operator_logo_url = snapshot.logo_url,
                operator_contact_phone = snapshot.contact_phone,
                operator_snapshot_resolved = TRUE,
                row_version = settlement.row_version + 1,
                updated_at = CURRENT_TIMESTAMP
            FROM snapshot_updates AS snapshot
            WHERE settlement.id = snapshot.settlement_id
              AND settlement.operator_snapshot_resolved = FALSE;
            """, cancellationToken);
    }

    private Task<int> ApplySettledBySnapshotsAsync(
        Guid[] settlementIds,
        Guid[] userIds,
        string?[] displayNames,
        string?[] emails,
        string?[] roles,
        CancellationToken cancellationToken)
    {
        if (settlementIds.Length == 0)
            return Task.FromResult(0);

        return _db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH snapshot_updates (settlement_id, user_id, display_name, email, role) AS (
                SELECT *
                FROM unnest(
                    {settlementIds}::uuid[],
                    {userIds}::uuid[],
                    {displayNames}::text[],
                    {emails}::text[],
                    {roles}::text[])
            )
            UPDATE vietride_payment.operator_trip_settlements AS settlement
            SET settled_by_display_name = snapshot.display_name,
                settled_by_email = snapshot.email,
                settled_by_role = snapshot.role,
                settled_by_snapshot_resolved = TRUE,
                row_version = settlement.row_version + 1,
                updated_at = CURRENT_TIMESTAMP
            FROM snapshot_updates AS snapshot
            WHERE settlement.id = snapshot.settlement_id
              AND settlement.settled_by_user_id = snapshot.user_id
              AND settlement.settled_by_snapshot_resolved = FALSE;
            """, cancellationToken);
    }

    private Task<int> ApplyPlatformActorSnapshotsAsync(
        Guid[] transactionIds,
        Guid[] userIds,
        string?[] displayNames,
        string?[] emails,
        string?[] roles,
        CancellationToken cancellationToken)
    {
        if (transactionIds.Length == 0)
            return Task.FromResult(0);

        return _db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH snapshot_updates (transaction_id, user_id, display_name, email, role) AS (
                SELECT *
                FROM unnest(
                    {transactionIds}::uuid[],
                    {userIds}::uuid[],
                    {displayNames}::text[],
                    {emails}::text[],
                    {roles}::text[])
            )
            UPDATE vietride_payment.platform_wallet_transactions AS transaction
            SET actor_display_name = snapshot.display_name,
                actor_email = snapshot.email,
                actor_role = snapshot.role,
                actor_snapshot_resolved = TRUE
            FROM snapshot_updates AS snapshot
            WHERE transaction.id = snapshot.transaction_id
              AND transaction.actor_user_id = snapshot.user_id
              AND transaction.actor_type = 'USER'
              AND transaction.actor_snapshot_resolved = FALSE;
            """, cancellationToken);
    }
}
