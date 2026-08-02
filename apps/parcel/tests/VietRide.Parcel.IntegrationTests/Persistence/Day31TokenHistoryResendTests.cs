using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.ResendDeliveryEmail;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.UnitOfWork;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.IntegrationTests.Persistence;

public sealed class Day31TokenHistoryResendTests
{
    [Fact]
    public async Task ResendRotation_PersistsHashOnlyResendAuditAndRevokesOldToken()
    {
        await WithDatabaseAsync(async dataSource =>
        {
            var parcel = CreateParcel("VRP-DAY31-RESEND");
            var oldRawToken = Guid.NewGuid();
            var oldIssuedAt = DateTimeOffset.UtcNow.AddHours(-2);
            var oldToken = ParcelDeliveryToken.Issue(
                parcel.Id,
                DeliveryTokenHasher.Hash(oldRawToken),
                oldIssuedAt.AddHours(48),
                Guid.NewGuid(),
                ParcelDeliveryTokenIssueReason.INITIAL_DELIVERY,
                oldIssuedAt);

            await SeedAsync(dataSource, parcel, ParcelStatus.DELIVERED_PENDING_CONFIRM, oldToken);

            var actorUserId = Guid.NewGuid();
            var resendStartedAt = DateTimeOffset.UtcNow;
            var emailClient = new CapturingDeliveryEmailClient();

            await using (var writeContext = CreateDbContext(dataSource))
            {
                var parcelRepository = CreateParcelRepository(writeContext);
                var tokenRepository = CreateTokenRepository(writeContext);
                var unitOfWork = new EfUnitOfWork(writeContext);
                var handler = new ResendDeliveryEmailCommandHandler(
                    parcelRepository,
                    tokenRepository,
                    null!,
                    emailClient,
                    null!,
                    null!,
                    unitOfWork);

                var response = await handler.Handle(
                    new ResendDeliveryEmailCommand(
                        parcel.Id,
                        actorUserId,
                        parcel.OperatorId,
                        "OPERATOR_STAFF"),
                    CancellationToken.None);
                response.ParcelId.Should().Be(parcel.Id);
                response.Status.Should().Be(ParcelStatus.DELIVERED_PENDING_CONFIRM.ToString());
                response.ExpiresAt.Should().Be(emailClient.Request!.ExpiresAt);
            }

            await using var verifyContext = CreateDbContext(dataSource);
            var verifiedAt = DateTimeOffset.UtcNow;
            emailClient.Request.Should().NotBeNull();
            var emailRequest = emailClient.Request!;
            var history = await verifyContext.ParcelDeliveryTokens
                .AsNoTracking()
                .Where(token => token.ParcelId == parcel.Id)
                .OrderBy(token => token.CreatedAt)
                .ToListAsync();

            history.Should().HaveCount(2);
            history[0].TokenHash.Should().Be(DeliveryTokenHasher.Hash(oldRawToken));
            history[0].RevokedAt.Should().BeOnOrAfter(resendStartedAt);
            history[1].Id.Should().Be(emailRequest.DeliveryTokenId);
            history[1].TokenHash.Should().Be(DeliveryTokenHasher.Hash(emailRequest.DeliveryToken));
            history[1].TokenHash.Should().NotContain(emailRequest.DeliveryToken.ToString("D"));
            history[1].IssueReason.Should().Be(ParcelDeliveryTokenIssueReason.RESEND);
            history[1].IssuedByUserId.Should().Be(actorUserId);
            history[1].CreatedAt.Should().BeOnOrAfter(resendStartedAt);
            history[1].CreatedAt.Should().BeOnOrBefore(verifiedAt);
            history[1].UpdatedAt.Should().Be(history[1].CreatedAt);
            history[1].RevokedAt.Should().BeNull();
        });
    }

    [Fact]
    public async Task RevokedOldToken_ExpiredToken_AndUndoWindow_AreEnforced()
    {
        await WithDatabaseAsync(async dataSource =>
        {
            var now = DateTimeOffset.UtcNow;
            var revokedParcel = CreateParcel("VRP-DAY31-REVOKED");
            var expiredParcel = CreateParcel("VRP-DAY31-EXPIRED");
            var undoParcel = CreateParcel("VRP-DAY31-UNDO");
            var revokedToken = ParcelDeliveryToken.Issue(
                revokedParcel.Id,
                DeliveryTokenHasher.Hash(Guid.NewGuid()),
                now.AddHours(48),
                Guid.NewGuid(),
                ParcelDeliveryTokenIssueReason.INITIAL_DELIVERY,
                now.AddMinutes(-1));
            revokedToken.Revoke(now);
            var expiredToken = ParcelDeliveryToken.Issue(
                expiredParcel.Id,
                DeliveryTokenHasher.Hash(Guid.NewGuid()),
                now.AddMinutes(-1),
                Guid.NewGuid(),
                ParcelDeliveryTokenIssueReason.INITIAL_DELIVERY,
                now.AddHours(-49));
            var undoToken = ParcelDeliveryToken.Issue(
                undoParcel.Id,
                DeliveryTokenHasher.Hash(Guid.NewGuid()),
                now.AddHours(48),
                Guid.NewGuid(),
                ParcelDeliveryTokenIssueReason.INITIAL_DELIVERY,
                now.AddMinutes(-1));

            await SeedAsync(dataSource, revokedParcel, ParcelStatus.DELIVERED_PENDING_CONFIRM, revokedToken);
            await SeedAsync(dataSource, expiredParcel, ParcelStatus.DELIVERED_PENDING_CONFIRM, expiredToken);
            await SeedAsync(dataSource, undoParcel, ParcelStatus.DELIVERY_REJECTED, undoToken, now.AddMinutes(-5));

            await using var context = CreateDbContext(dataSource);
            var repository = CreateParcelRepository(context);

            (await repository.TryConfirmDeliveryAsync(
                revokedParcel.Id,
                revokedToken.Id,
                "127.0.0.1",
                now,
                CancellationToken.None)).Should().BeNull();
            (await repository.TryConfirmDeliveryAsync(
                expiredParcel.Id,
                expiredToken.Id,
                "127.0.0.1",
                now,
                CancellationToken.None)).Should().BeNull();

            var undone = await repository.TryUndoRejectDeliveryAsync(
                undoParcel.Id,
                undoToken.Id,
                now,
                CancellationToken.None);
            undone.Should().NotBeNull();
            undone!.Status.Should().Be(ParcelStatus.DELIVERED_PENDING_CONFIRM);

            var confirmed = await repository.TryConfirmDeliveryAsync(
                undoParcel.Id,
                undoToken.Id,
                "127.0.0.1",
                now.AddSeconds(1),
                CancellationToken.None);
            confirmed.Should().NotBeNull();
            confirmed!.Status.Should().Be(ParcelStatus.DELIVERY_CONFIRMED);
        });
    }

    [Fact]
    public async Task ManualConfirm_ConcurrentReplay_HasOneWinnerAndStableAuditSnapshot()
    {
        await WithDatabaseAsync(async dataSource =>
        {
            var parcel = CreateParcel("VRP-DAY31-MANUAL");
            var rawToken = Guid.NewGuid();
            var issuedAt = DateTimeOffset.UtcNow.AddHours(-1);
            var token = ParcelDeliveryToken.Issue(
                parcel.Id,
                DeliveryTokenHasher.Hash(rawToken),
                issuedAt.AddHours(48),
                Guid.NewGuid(),
                ParcelDeliveryTokenIssueReason.INITIAL_DELIVERY,
                issuedAt);
            await SeedAsync(dataSource, parcel, ParcelStatus.DELIVERED_PENDING_CONFIRM, token);

            var actorUserId = Guid.NewGuid();
            const string note = "Recipient confirmed by phone";
            var attemptedAt = new[]
            {
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMilliseconds(1),
            };

            await using var firstContext = CreateDbContext(dataSource);
            await using var secondContext = CreateDbContext(dataSource);
            var attempts = await Task.WhenAll(
                CreateParcelRepository(firstContext).TryManualConfirmDeliveryAsync(
                    parcel.Id,
                    parcel.OperatorId,
                    actorUserId,
                    note,
                    attemptedAt[0],
                    CancellationToken.None),
                CreateParcelRepository(secondContext).TryManualConfirmDeliveryAsync(
                    parcel.Id,
                    parcel.OperatorId,
                    actorUserId,
                    note,
                    attemptedAt[1],
                    CancellationToken.None));

            attempts.Should().ContainSingle(result => result != null);

            await using (var revokeContext = CreateDbContext(dataSource))
            {
                (await CreateTokenRepository(revokeContext).RevokeActiveAsync(
                    parcel.Id,
                    attemptedAt.Max(),
                    CancellationToken.None)).Should().BeTrue();
            }

            await using var verifyContext = CreateDbContext(dataSource);
            var repository = CreateParcelRepository(verifyContext);
            var snapshot = await repository.GetManualConfirmationSnapshotAsync(
                parcel.Id,
                CancellationToken.None);
            snapshot.Should().NotBeNull();
            snapshot!.Status.Should().Be(ParcelStatus.DELIVERY_CONFIRMED);
            snapshot.ConfirmedByUserId.Should().Be(actorUserId);
            snapshot.ConfirmNote.Should().Be(note);
            snapshot.ConfirmedAt.Should().BeCloseTo(
                attemptedAt[0],
                TimeSpan.FromMilliseconds(5));

            (await repository.TryManualConfirmDeliveryAsync(
                parcel.Id,
                parcel.OperatorId,
                actorUserId,
                note,
                attemptedAt.Max().AddSeconds(1),
                CancellationToken.None)).Should().BeNull();
            (await verifyContext.ParcelDeliveryTokens
                .AsNoTracking()
                .SingleAsync(history => history.Id == token.Id))
                .RevokedAt.Should().NotBeNull();
        });
    }

    private static async Task SeedAsync(
        NpgsqlDataSource dataSource,
        ParcelEntity parcel,
        ParcelStatus status,
        ParcelDeliveryToken token,
        DateTimeOffset? rejectedAt = null)
    {
        await using var context = CreateDbContext(dataSource);
        context.Parcels.Add(parcel);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_parcel.parcels
            SET status = CAST({status.ToString()} AS vietride_parcel.parcel_status),
                rejected_at = {rejectedAt},
                rejection_reason = {(rejectedAt.HasValue ? "Recipient rejected" : null)}
            WHERE id = {parcel.Id};
            """);
        context.ParcelDeliveryTokens.Add(token);
        await context.SaveChangesAsync();
    }

    private static ParcelEntity CreateParcel(string parcelCode)
        => ParcelEntity.CreatePendingPayment(
            parcelCode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            "recipient@example.com",
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

    private static IParcelRepository CreateParcelRepository(ParcelDbContext dbContext)
        => (IParcelRepository)Activator.CreateInstance(
            typeof(ParcelDbContext).Assembly.GetType(
                "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelRepository",
                throwOnError: true)!,
            dbContext)!;

    private static IParcelDeliveryTokenRepository CreateTokenRepository(ParcelDbContext dbContext)
        => (IParcelDeliveryTokenRepository)Activator.CreateInstance(
            typeof(ParcelDbContext).Assembly.GetType(
                "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelDeliveryTokenRepository",
                throwOnError: true)!,
            dbContext)!;

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        ParcelDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    private static ParcelDbContext CreateDbContext(NpgsqlDataSource dataSource)
        => new(
            ParcelIntegrationDbContextOptions.Create(dataSource),
            new SystemClock());

    private static async Task WithDatabaseAsync(Func<NpgsqlDataSource, Task> assertion)
    {
        var databaseName = $"vietride_parcel_day31_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            await using (var context = CreateDbContext(dataSource))
            {
                await context.Database.MigrateAsync();
            }

            await assertion(dataSource);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string defaultConnectionString =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_PARCEL_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configured)
            ? defaultConnectionString
            : configured;
        connectionString = connectionString.Replace(
            "{databaseName}",
            databaseName,
            StringComparison.OrdinalIgnoreCase);
        return new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class CapturingDeliveryEmailClient : IParcelDeliveryEmailClient
    {
        public ParcelDeliveryEmailRequest? Request { get; private set; }

        public Task SendDeliveryLinkAsync(
            ParcelDeliveryEmailRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.CompletedTask;
        }
    }
}
