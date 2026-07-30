using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.IntegrationTests.Persistence;

public sealed class ParcelStatusHistoryPersistenceTests
{
    private const string PreviousMigration = "20260729225848_AddParcelTripDisplaySnapshots";
    private const string TargetMigration = "20260730001713_AddParcelStatusHistory";

    [Fact]
    public async Task Trigger_CapturesOnlyRealTransitionsAndHistoryIsImmutable()
    {
        var databaseName = $"vietride_parcel_ui13_history_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var legacyParcel = CreateParcel("LEGACY");
            var rolloutRaceParcel = CreateParcel("ROLLOUT");

            await using (var context = CreateDbContext(dataSource))
            {
                var migrator = context.GetService<IMigrator>();
                await migrator.MigrateAsync(PreviousMigration);
                context.Parcels.AddRange(legacyParcel, rolloutRaceParcel);
                await context.SaveChangesAsync();
                var (migrationStartedAt, migrationCompletedAt) =
                    await AssertRolloutLockClosesTransitionGapAsync(
                        dataSource,
                        migrator,
                        rolloutRaceParcel.Id);

                var baseline = await ReadHistoryAsync(dataSource, legacyParcel.Id);
                baseline.Should().ContainSingle();
                baseline[0].Status.Should().Be(ParcelStatus.PENDING_PAYMENT.ToString());
                baseline[0].OccurredAt.Should().BeOnOrAfter(migrationStartedAt);
                baseline[0].OccurredAt.Should().BeOnOrBefore(migrationCompletedAt);
                baseline[0].ActorType.Should().Be("SYSTEM");
                baseline[0].ActorId.Should().BeNull();
                baseline[0].Source.Should().Be("MIGRATION_BASELINE");
                baseline[0].Reason.Should().BeNull();

                var rolloutHistory = await ReadHistoryAsync(dataSource, rolloutRaceParcel.Id);
                rolloutHistory.Select(item => item.Status).Should().Equal(
                    ParcelStatus.PENDING_PAYMENT.ToString(),
                    ParcelStatus.CHECKED_IN.ToString());
                rolloutHistory.Select(item => item.Source).Should().Equal(
                    "MIGRATION_BASELINE",
                    "STATUS_TRIGGER");
            }

            var newParcel = CreateParcel("NEW");
            await using (var context = CreateDbContext(dataSource))
            {
                context.Parcels.Add(newParcel);
                await context.SaveChangesAsync();
            }

            (await ReadHistoryAsync(dataSource, newParcel.Id)).Should().BeEmpty();

            var actorId = Guid.NewGuid();
            var checkedInAt = DateTimeOffset.UtcNow;
            var transitionStartedAt = DateTimeOffset.UtcNow;
            await using (var context = CreateDbContext(dataSource))
            {
                var affected = await context.Parcels
                    .Where(parcel => parcel.Id == newParcel.Id
                        && parcel.Status == ParcelStatus.PENDING_PAYMENT)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(parcel => parcel.Status, ParcelStatus.CHECKED_IN)
                        .SetProperty(parcel => parcel.CheckedInAt, checkedInAt)
                        .SetProperty(parcel => parcel.CheckedInByUserId, actorId)
                        .SetProperty(parcel => parcel.UpdatedAt, checkedInAt));
                affected.Should().Be(1);
            }
            var transitionCompletedAt = DateTimeOffset.UtcNow;

            var checkedInHistory = await ReadHistoryAsync(dataSource, newParcel.Id);
            checkedInHistory.Should().ContainSingle();
            checkedInHistory[0].Status.Should().Be(ParcelStatus.CHECKED_IN.ToString());
            checkedInHistory[0].OccurredAt.Should().BeOnOrAfter(transitionStartedAt);
            checkedInHistory[0].OccurredAt.Should().BeOnOrBefore(transitionCompletedAt);
            checkedInHistory[0].ActorType.Should().Be("USER");
            checkedInHistory[0].ActorId.Should().Be(actorId);
            checkedInHistory[0].Source.Should().Be("STATUS_TRIGGER");
            checkedInHistory[0].Reason.Should().BeNull();

            await using (var context = CreateDbContext(dataSource))
            {
                var mapped = await context.ParcelStatusHistories.AsNoTracking()
                    .SingleAsync(history => history.ParcelId == newParcel.Id);
                mapped.Status.Should().Be(ParcelStatus.CHECKED_IN);
                mapped.ActorId.Should().Be(actorId);
            }

            await using (var context = CreateDbContext(dataSource))
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE vietride_parcel.parcels
                    SET status = status,
                        pending_action_reason = {"metadata-only update"}
                    WHERE id = {newParcel.Id};
                    """);
            }

            (await ReadHistoryAsync(dataSource, newParcel.Id)).Should().ContainSingle();

            await using (var context = CreateDbContext(dataSource))
            {
                var affected = await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE vietride_parcel.parcels
                    SET status = 'CANCELLED'::vietride_parcel.parcel_status,
                        cancellation_reason = {"sender-requested"}
                    WHERE id = {newParcel.Id}
                      AND status = 'CHECKED_IN'::vietride_parcel.parcel_status;
                    """);
                affected.Should().Be(1);
            }

            var casResults = await Task.WhenAll(
                TryRawTransitionAsync(dataSource, newParcel.Id, "cas-a"),
                TryRawTransitionAsync(dataSource, newParcel.Id, "cas-b"));
            casResults.Sum().Should().Be(1);

            var ordered = await ReadHistoryAsync(dataSource, newParcel.Id);
            ordered.Select(item => item.Status).Should().Equal(
                ParcelStatus.CHECKED_IN.ToString(),
                ParcelStatus.CANCELLED.ToString(),
                ParcelStatus.REJECTED.ToString());
            ordered[1].ActorType.Should().Be("UNKNOWN");
            ordered[1].ActorId.Should().BeNull();
            ordered[1].Source.Should().Be("STATUS_TRIGGER");
            ordered[1].Reason.Should().Be("sender-requested");
            ordered[2].Reason.Should().BeOneOf("cas-a", "cas-b");
            ordered.Select(item => item.Id).Should().Equal(
                ordered.OrderBy(item => item.OccurredAt).ThenBy(item => item.Id).Select(item => item.Id));

            await AssertMutationRejectedAsync(dataSource, ordered[0].Id, "UPDATE");
            await AssertMutationRejectedAsync(dataSource, ordered[0].Id, "DELETE");
            await AssertDuplicateBaselineRejectedAsync(dataSource, legacyParcel.Id);
            await AssertParcelDeleteRestrictedAsync(dataSource, newParcel.Id);
            await AssertActorAndReasonMatrixAsync(dataSource);
            await AssertMigrationRoundTripAsync(dataSource, legacyParcel.Id, newParcel.Id);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static async Task<(DateTimeOffset StartedAt, DateTimeOffset CompletedAt)>
        AssertRolloutLockClosesTransitionGapAsync(
            NpgsqlDataSource dataSource,
            IMigrator migrator,
            Guid parcelId)
    {
        const string lockStatement =
            "LOCK TABLE vietride_parcel.parcels IN SHARE ROW EXCLUSIVE MODE;";
        var script = migrator.GenerateScript(PreviousMigration, TargetMigration);
        var lockIndex = script.IndexOf(lockStatement, StringComparison.Ordinal);
        var baselineIndex = script.IndexOf(
            "INSERT INTO vietride_parcel.parcel_status_history",
            StringComparison.Ordinal);
        var triggerIndex = script.IndexOf(
            "CREATE TRIGGER trg_parcels_status_history",
            StringComparison.Ordinal);
        lockIndex.Should().BeGreaterThanOrEqualTo(0);
        baselineIndex.Should().BeGreaterThan(lockIndex);
        triggerIndex.Should().BeGreaterThan(baselineIndex);

        var delayedScript = script.Replace(
            lockStatement,
            $"{lockStatement}{Environment.NewLine}SELECT pg_sleep(1);",
            StringComparison.Ordinal);
        var startedAt = DateTimeOffset.UtcNow;
        await using var migrationConnection = await dataSource.OpenConnectionAsync();
        await using var migrationCommand = new NpgsqlCommand(delayedScript, migrationConnection)
        {
            CommandTimeout = 30,
        };
        var migrationTask = migrationCommand.ExecuteNonQueryAsync();
        await WaitForRolloutLockAsync(dataSource);

        await using var updateContext = CreateDbContext(dataSource);
        var updateTask = updateContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_parcel.parcels
            SET status = 'CHECKED_IN'::vietride_parcel.parcel_status
            WHERE id = {parcelId};
            """);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        updateTask.IsCompleted.Should().BeFalse(
            "the migration lock must block status writers until the trigger is installed");

        await migrationTask;
        var completedAt = DateTimeOffset.UtcNow;
        (await updateTask).Should().Be(1);
        return (startedAt, completedAt);
    }

    private static async Task WaitForRolloutLockAsync(NpgsqlDataSource dataSource)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_locks AS held_lock
                JOIN pg_class AS relation ON relation.oid = held_lock.relation
                JOIN pg_namespace AS relation_schema ON relation_schema.oid = relation.relnamespace
                WHERE relation_schema.nspname = 'vietride_parcel'
                  AND relation.relname = 'parcels'
                  AND held_lock.mode = 'ShareRowExclusiveLock'
                  AND held_lock.granted
            );
            """;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = dataSource.CreateCommand(sql);
            if (Convert.ToBoolean(await command.ExecuteScalarAsync()))
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new TimeoutException("The Parcel migration did not acquire its rollout lock.");
    }

    private static async Task<int> TryRawTransitionAsync(
        NpgsqlDataSource dataSource,
        Guid parcelId,
        string reason)
    {
        await using var context = CreateDbContext(dataSource);
        return await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_parcel.parcels
            SET status = 'REJECTED'::vietride_parcel.parcel_status,
                rejection_reason = {reason}
            WHERE id = {parcelId}
              AND status = 'CANCELLED'::vietride_parcel.parcel_status;
            """);
    }

    private static async Task AssertMutationRejectedAsync(
        NpgsqlDataSource dataSource,
        Guid historyId,
        string operation)
    {
        var sql = operation == "UPDATE"
            ? "UPDATE vietride_parcel.parcel_status_history SET source = 'TAMPERED' WHERE id = @id;"
            : "DELETE FROM vietride_parcel.parcel_status_history WHERE id = @id;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", historyId);

        var act = async () => await command.ExecuteNonQueryAsync();
        var exception = await act.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be("55000");
    }

    private static async Task AssertDuplicateBaselineRejectedAsync(
        NpgsqlDataSource dataSource,
        Guid parcelId)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO vietride_parcel.parcel_status_history
                (parcel_id, status, occurred_at, actor_type, source)
            SELECT id, status, now(), 'SYSTEM', 'MIGRATION_BASELINE'
            FROM vietride_parcel.parcels
            WHERE id = @parcel_id;
            """);
        command.Parameters.AddWithValue("parcel_id", parcelId);

        var act = async () => await command.ExecuteNonQueryAsync();
        var exception = await act.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be("23505");
    }

    private static async Task AssertParcelDeleteRestrictedAsync(
        NpgsqlDataSource dataSource,
        Guid parcelId)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM vietride_parcel.parcels WHERE id = @parcel_id;");
        command.Parameters.AddWithValue("parcel_id", parcelId);

        var act = async () => await command.ExecuteNonQueryAsync();
        var exception = await act.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be("23503");
    }

    private static async Task AssertActorAndReasonMatrixAsync(NpgsqlDataSource dataSource)
    {
        var userParcel = CreateParcel("USERS");
        var manualConfirmParcel = CreateParcel("MANUAL");
        var recipientConfirmParcel = CreateParcel("TOKEN-OK");
        var recipientRejectParcel = CreateParcel("TOKEN-NO");
        var pendingActionParcel = CreateParcel("PENDING");
        await using (var seed = CreateDbContext(dataSource))
        {
            seed.Parcels.AddRange(
                userParcel,
                manualConfirmParcel,
                recipientConfirmParcel,
                recipientRejectParcel,
                pendingActionParcel);
            await seed.SaveChangesAsync();
        }

        var reviewedBy = Guid.NewGuid();
        var checkedInBy = Guid.NewGuid();
        var reweighedBy = Guid.NewGuid();
        var loadedBy = Guid.NewGuid();
        var transferConfirmedBy = Guid.NewGuid();
        var returnedBy = Guid.NewGuid();
        var userTimelineStart = DateTimeOffset.UtcNow;
        await using (var context = CreateDbContext(dataSource))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_parcel.parcels
                SET status = 'PENDING_OPERATOR_REVIEW'::vietride_parcel.parcel_status,
                    updated_at = {userTimelineStart}
                WHERE id = {userParcel.Id};
                UPDATE vietride_parcel.parcels
                SET status = 'PENDING_PAYMENT'::vietride_parcel.parcel_status,
                    reviewed_by_user_id = {reviewedBy},
                    updated_at = {userTimelineStart.AddSeconds(1)}
                WHERE id = {userParcel.Id};
                UPDATE vietride_parcel.parcels
                SET status = 'CHECKED_IN'::vietride_parcel.parcel_status,
                    checked_in_by_user_id = {checkedInBy},
                    updated_at = {userTimelineStart.AddSeconds(2)}
                WHERE id = {userParcel.Id};
                UPDATE vietride_parcel.parcels
                SET status = 'READY_TO_LOAD'::vietride_parcel.parcel_status,
                    reweighed_by_user_id = {reweighedBy},
                    updated_at = {userTimelineStart.AddSeconds(3)}
                WHERE id = {userParcel.Id};
                UPDATE vietride_parcel.parcels
                SET status = 'LOADED'::vietride_parcel.parcel_status,
                    loaded_by_user_id = {loadedBy},
                    updated_at = {userTimelineStart.AddSeconds(4)}
                WHERE id = {userParcel.Id};
                UPDATE vietride_parcel.parcels
                SET status = 'PENDING_TRANSFER_CONFIRM'::vietride_parcel.parcel_status,
                    updated_at = {userTimelineStart.AddSeconds(5)}
                WHERE id = {userParcel.Id};
                UPDATE vietride_parcel.parcels
                SET status = 'LOADED'::vietride_parcel.parcel_status,
                    transfer_confirmed_by_user_id = {transferConfirmedBy},
                    updated_at = {userTimelineStart.AddSeconds(6)}
                WHERE id = {userParcel.Id};
                UPDATE vietride_parcel.parcels
                SET status = 'RETURN_INITIATED'::vietride_parcel.parcel_status,
                    updated_at = {userTimelineStart.AddSeconds(7)}
                WHERE id = {userParcel.Id};
                UPDATE vietride_parcel.parcels
                SET status = 'RETURNED'::vietride_parcel.parcel_status,
                    returned_by_user_id = {returnedBy},
                    return_reason = {"recipient-unavailable"},
                    updated_at = {userTimelineStart.AddSeconds(8)}
                WHERE id = {userParcel.Id};
                """);
        }

        var userHistory = await ReadHistoryAsync(dataSource, userParcel.Id);
        AssertUserActor(userHistory, ParcelStatus.PENDING_PAYMENT, reviewedBy);
        AssertUserActor(userHistory, ParcelStatus.CHECKED_IN, checkedInBy);
        AssertUserActor(userHistory, ParcelStatus.READY_TO_LOAD, reweighedBy);
        userHistory.Where(item => item.Status == ParcelStatus.LOADED.ToString())
            .Select(item => item.ActorId).Should().BeEquivalentTo(
                new Guid?[] { loadedBy, transferConfirmedBy });
        AssertUserActor(userHistory, ParcelStatus.RETURNED, returnedBy);
        userHistory.Last(item => item.Status == ParcelStatus.RETURNED.ToString())
            .Reason.Should().Be("recipient-unavailable");

        var confirmedBy = Guid.NewGuid();
        var deliveryTimelineStart = userTimelineStart.AddMinutes(1);
        await using (var context = CreateDbContext(dataSource))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_parcel.parcels
                SET status = 'DELIVERED_PENDING_CONFIRM'::vietride_parcel.parcel_status,
                    updated_at = {deliveryTimelineStart}
                WHERE id = {manualConfirmParcel.Id};
                UPDATE vietride_parcel.parcels
                SET status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status,
                    confirmed_by_user_id = {confirmedBy},
                    updated_at = {deliveryTimelineStart.AddSeconds(1)}
                WHERE id = {manualConfirmParcel.Id};

                UPDATE vietride_parcel.parcels
                SET status = 'DELIVERED_PENDING_CONFIRM'::vietride_parcel.parcel_status,
                    updated_at = {deliveryTimelineStart}
                WHERE id = {recipientConfirmParcel.Id};
                UPDATE vietride_parcel.parcels
                SET status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status,
                    confirmed_by_ip = {"203.0.113.10"},
                    updated_at = {deliveryTimelineStart.AddSeconds(1)}
                WHERE id = {recipientConfirmParcel.Id};

                UPDATE vietride_parcel.parcels
                SET status = 'PENDING_OPERATOR_ACTION'::vietride_parcel.parcel_status,
                    pending_action_reason = {"operator-review-required"},
                    updated_at = {deliveryTimelineStart}
                WHERE id = {pendingActionParcel.Id};
                """);
        }

        AssertUserActor(
            await ReadHistoryAsync(dataSource, manualConfirmParcel.Id),
            ParcelStatus.DELIVERY_CONFIRMED,
            confirmedBy);
        (await ReadHistoryAsync(dataSource, recipientConfirmParcel.Id)).Last().ActorType
            .Should().Be("RECIPIENT");
        var pendingAction = (await ReadHistoryAsync(dataSource, pendingActionParcel.Id)).Last();
        pendingAction.ActorType.Should().Be("UNKNOWN");
        pendingAction.Reason.Should().Be("operator-review-required");

        var tiedAt = DateTimeOffset.UtcNow;
        await using (var context = CreateDbContext(dataSource))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_parcel.parcels
                SET status = 'DELIVERED_PENDING_CONFIRM'::vietride_parcel.parcel_status,
                    updated_at = {tiedAt.AddSeconds(-1)}
                WHERE id = {recipientRejectParcel.Id};
                UPDATE vietride_parcel.parcels
                SET status = 'DELIVERY_REJECTED'::vietride_parcel.parcel_status,
                    rejection_reason = {"damaged"},
                    updated_at = {tiedAt}
                WHERE id = {recipientRejectParcel.Id};
                UPDATE vietride_parcel.parcels
                SET status = 'DELIVERED_PENDING_CONFIRM'::vietride_parcel.parcel_status,
                    updated_at = {tiedAt}
                WHERE id = {recipientRejectParcel.Id};
                """);
        }

        var firstRead = await ReadHistoryAsync(dataSource, recipientRejectParcel.Id);
        var secondRead = await ReadHistoryAsync(dataSource, recipientRejectParcel.Id);
        firstRead.Select(item => item.Id).Should().Equal(secondRead.Select(item => item.Id));
        var recipientRows = firstRead.TakeLast(2).ToArray();
        recipientRows.Should().OnlyContain(item => item.ActorType == "RECIPIENT");
        recipientRows[0].Reason.Should().Be("damaged");
        recipientRows[1].Reason.Should().BeNull();
        recipientRows.Select(item => item.Id).Should().Equal(
            recipientRows.OrderBy(item => item.OccurredAt).ThenBy(item => item.Id).Select(item => item.Id));
    }

    private static void AssertUserActor(
        IReadOnlyCollection<StatusHistoryRow> history,
        ParcelStatus status,
        Guid actorId)
    {
        var item = history.Last(row => row.Status == status.ToString());
        item.ActorType.Should().Be("USER");
        item.ActorId.Should().Be(actorId);
    }

    private static async Task AssertMigrationRoundTripAsync(
        NpgsqlDataSource dataSource,
        params Guid[] parcelIds)
    {
        await using var context = CreateDbContext(dataSource);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        await using (var command = dataSource.CreateCommand(
            "SELECT to_regclass('vietride_parcel.parcel_status_history') IS NULL;"))
        {
            Convert.ToBoolean(await command.ExecuteScalarAsync()).Should().BeTrue();
        }

        await migrator.MigrateAsync();
        var baselines = new List<StatusHistoryRow>();
        foreach (var parcelId in parcelIds)
            baselines.AddRange(await ReadHistoryAsync(dataSource, parcelId));

        baselines.Should().HaveCount(parcelIds.Length);
        baselines.Should().OnlyContain(item => item.Source == "MIGRATION_BASELINE");
    }

    private static async Task<IReadOnlyList<StatusHistoryRow>> ReadHistoryAsync(
        NpgsqlDataSource dataSource,
        Guid parcelId)
    {
        const string sql = """
            SELECT id, status::text, occurred_at, actor_type, actor_id, source, reason
            FROM vietride_parcel.parcel_status_history
            WHERE parcel_id = @parcel_id
            ORDER BY occurred_at, id;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("parcel_id", parcelId);
        await using var reader = await command.ExecuteReaderAsync();
        var items = new List<StatusHistoryRow>();
        while (await reader.ReadAsync())
        {
            items.Add(new StatusHistoryRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return items;
    }

    private static ParcelEntity CreateParcel(string marker)
        => ParcelEntity.CreatePendingPayment(
            ($"VRP-UI13-{marker}-{Guid.NewGuid():N}")[..30],
            Guid.NewGuid(),
            null,
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            "Item",
            null,
            ParcelSizeCategory.MEDIUM,
            5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        ParcelDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    private static ParcelDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = new DbContextOptionsBuilder<ParcelDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ParcelDbContext.SchemaName))
            .Options;
        return new ParcelDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_PARCEL_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return new NpgsqlConnectionStringBuilder(
            connectionString.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase))
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record StatusHistoryRow(
        Guid Id,
        string Status,
        DateTimeOffset OccurredAt,
        string ActorType,
        Guid? ActorId,
        string Source,
        string? Reason);
}
