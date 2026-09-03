using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Reliability.Claims;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Application.Behaviors;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.IntegrationTests.Persistence;

public sealed class ParcelCompensationFinancialPolicyPersistenceTests
{
    [Theory]
    [InlineData(ParcelClaimProofStatus.VERIFIED)]
    [InlineData(ParcelClaimProofStatus.NO_PROOF)]
    [InlineData(ParcelClaimProofStatus.UNVERIFIED)]
    public async Task MinimalParcelFlow_PersistsProofAudit_AndPaysOnlyPositiveAppealDelta(ParcelClaimProofStatus claimProof)
    {
        var verified = claimProof == ParcelClaimProofStatus.VERIFIED;
        var expectedOriginalAward = verified ? 200_000 : 150_000;
        var expectedDelta = 300_000 - expectedOriginalAward;
        var databaseName = $"vietride_parcel_compensation_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var operatorId = Guid.NewGuid();
            var operatorAdminId = Guid.NewGuid();
            var driverId = Guid.NewGuid();
            var assistantId = Guid.NewGuid();
            var senderId = Guid.NewGuid();
            var tripId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var parcel = CreateParcel(operatorId, tripId, senderId);
            parcel.AcceptDeclaration(300_000, declarationPolicyVersion: 1, acceptedAt: now);
            var incident = ParcelIncident.Open(
                parcel.Id,
                operatorId,
                ParcelIncidentType.DAMAGED,
                searchDeadline: null,
                tripId,
                legId: null,
                reporterId: assistantId,
                reporterSource: "ASSISTANT",
                expectedLocation: null,
                lastKnownLocation: $"TRIP:{tripId:D};DRIVER:{driverId:D}",
                description: "Minimal compensation policy test incident.",
                evidenceJson: null,
                operatorProcessBreach: false);
            var claim = ParcelClaim.Submit(
                parcel.Id,
                incident.Id,
                operatorId,
                senderId,
                declaredValueVnd: 300_000,
                policyVersion: 1,
                compensationRatePercent: 50,
                policyCapVnd: 30_000_000,
                noProofFallbackMultiplier: 2);
            var evidence = ParcelClaimEvidence.Create(
                claim.Id,
                "INVOICE",
                "https://example.test/invoice-001",
                "Accepted purchase invoice.",
                senderId);

            await using (var seedContext = CreateDbContext(dataSource))
            {
                await seedContext.Database.MigrateAsync();
                seedContext.Parcels.Add(parcel);
                seedContext.ParcelIncidents.Add(incident);
                seedContext.ParcelClaims.Add(claim);
                seedContext.ParcelClaimEvidence.Add(evidence);
                await seedContext.SaveChangesAsync();
                await seedContext.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE vietride_parcel.parcels
                    SET final_total_price_vnd = {150_000L}
                    WHERE id = {parcel.Id};
                    """);
            }

            await using (var previewContext = CreateDbContext(dataSource))
            {
                var parcelRepository = CreateParcelRepository(previewContext);
                var reliabilityRepository = CreateReliabilityRepository(previewContext);
                var previewHandler = new PreviewParcelClaimAwardQueryHandler(
                    parcelRepository,
                    reliabilityRepository);

                var noProofPreview = await previewHandler.Handle(
                    new PreviewParcelClaimAwardQuery(
                        claim.Id,
                        operatorId,
                        ParcelClaimProofStatus.NO_PROOF.ToString(),
                        ProvenDirectLossVnd: null,
                        AcceptedEvidenceIds: []),
                    CancellationToken.None);

                noProofPreview.CalculationBasis.Should().Be("NO_VERIFIED_PROOF_FREIGHT_ONLY");
                noProofPreview.FallbackAmountVnd.Should().BeNull();
                noProofPreview.DeclaredLiabilityVnd.Should().Be(150_000);
                noProofPreview.CargoAwardVnd.Should().Be(0);
                noProofPreview.FreightRefundVnd.Should().Be(150_000);
                noProofPreview.TotalAwardVnd.Should().Be(150_000);

                var wrongTenantAction = async () => await previewHandler.Handle(
                    new PreviewParcelClaimAwardQuery(
                        claim.Id,
                        Guid.NewGuid(),
                        ParcelClaimProofStatus.NO_PROOF.ToString(),
                        ProvenDirectLossVnd: null,
                        AcceptedEvidenceIds: []),
                    CancellationToken.None);
                var tenantMasked = await wrongTenantAction.Should().ThrowAsync<CodedNotFoundException>();
                tenantMasked.Which.ErrorCode.Should().Be("PARCEL_CLAIM_NOT_FOUND");

                var missingEvidenceAction = async () => await previewHandler.Handle(
                    new PreviewParcelClaimAwardQuery(
                        claim.Id,
                        operatorId,
                        ParcelClaimProofStatus.VERIFIED.ToString(),
                        ProvenDirectLossVnd: 100_000,
                        AcceptedEvidenceIds: [Guid.NewGuid()]),
                    CancellationToken.None);
                var missingEvidence = await missingEvidenceAction.Should().ThrowAsync<CodedNotFoundException>();
                missingEvidence.Which.ErrorCode.Should().Be("PARCEL_CLAIM_EVIDENCE_NOT_FOUND");

                var verifiedPreview = await previewHandler.Handle(
                    new PreviewParcelClaimAwardQuery(
                        claim.Id,
                        operatorId,
                        ParcelClaimProofStatus.VERIFIED.ToString(),
                        ProvenDirectLossVnd: 100_000,
                        AcceptedEvidenceIds: [evidence.Id]),
                    CancellationToken.None);
                verifiedPreview.CargoAwardVnd.Should().Be(50_000);
                verifiedPreview.FreightRefundVnd.Should().Be(150_000);
                verifiedPreview.TotalAwardVnd.Should().Be(200_000);
            }

            ParcelClaimResponse claimDecision;
            await using (var decisionContext = CreateDbContext(dataSource))
            {
                claimDecision = await ExecuteClaimDecisionAsync(
                    decisionContext,
                    new DecideParcelClaimCommand(
                        claim.Id,
                        operatorId,
                        operatorAdminId,
                        "APPROVE",
                        claimProof.ToString(),
                        ProvenDirectLossVnd: verified ? 100_000 : null,
                        AcceptedEvidenceIds: verified ? [evidence.Id] : [],
                        "Compensation according to the assessed proof status."));
            }

            claimDecision.Status.Should().Be(ParcelClaimStatus.APPROVED.ToString());
            claimDecision.ProofStatus.Should().Be(claimProof.ToString());
            claimDecision.AcceptedEvidenceIds.Should().Equal(verified ? [evidence.Id] : Array.Empty<Guid>());
            claimDecision.CargoAwardVnd.Should().Be(verified ? 50_000 : 0);
            claimDecision.TotalAwardVnd.Should().Be(expectedOriginalAward);

            ParcelClaimAppeal appeal;
            await using (var appealSeedContext = CreateDbContext(dataSource))
            {
                var paidClaim = await appealSeedContext.ParcelClaims.SingleAsync(item => item.Id == claim.Id);
                paidClaim.MarkPaid(Guid.NewGuid(), now.AddMinutes(1));
                appeal = ParcelClaimAppeal.Submit(
                    paidClaim,
                    "The accepted invoice supports the full declared loss.",
                    senderId,
                    now.AddMinutes(2),
                    Guid.NewGuid());
                appealSeedContext.ParcelClaimAppeals.Add(appeal);
                await appealSeedContext.SaveChangesAsync();
            }

            // Repeating self-declaration in an appeal must not produce another freight payout.
            await using (var invalidDecisionContext = CreateDbContext(dataSource))
            {
                var action = () => ExecuteAppealDecisionAsync(invalidDecisionContext,
                    new DecideParcelClaimAppealCommand(appeal.Id, operatorId, operatorAdminId,
                        "APPROVE_ADJUSTMENT", "NO_PROOF", null, [], "No verified evidence."));
                (await action.Should().ThrowAsync<CodedValidationException>()).Which.ErrorCode
                    .Should().Be("PARCEL_CLAIM_APPEAL_ADJUSTMENT_REQUIRED");
            }
            await using (var rollbackContext = CreateDbContext(dataSource))
            {
                (await rollbackContext.ParcelClaimAppeals.AsNoTracking().SingleAsync(item => item.Id == appeal.Id))
                    .Status.Should().Be(ParcelClaimAppealStatus.SUBMITTED);
                (await rollbackContext.OutboxEvents.CountAsync(item =>
                    item.EventType == ParcelOutboxEvents.ParcelClaimAppealDecided)).Should().Be(0);
                (await rollbackContext.ParcelClaimAppealDecisionEvidence.CountAsync()).Should().Be(0);
            }

            await using (var appealPreviewContext = CreateDbContext(dataSource))
            {
                var appealPreviewHandler = new PreviewParcelClaimAppealAdjustmentQueryHandler(
                    CreateParcelRepository(appealPreviewContext),
                    CreateReliabilityRepository(appealPreviewContext));
                var appealPreview = await appealPreviewHandler.Handle(
                    new PreviewParcelClaimAppealAdjustmentQuery(
                        appeal.Id,
                        operatorId,
                        ParcelClaimProofStatus.VERIFIED.ToString(),
                        RevisedProvenDirectLossVnd: 300_000,
                        AcceptedEvidenceIds: [evidence.Id]),
                    CancellationToken.None);

                appealPreview.OriginalTotalAwardVnd.Should().Be(expectedOriginalAward);
                appealPreview.CargoAwardVnd.Should().Be(150_000);
                appealPreview.FreightRefundVnd.Should().Be(150_000);
                appealPreview.TotalAwardVnd.Should().Be(300_000);
                appealPreview.SupplementaryAwardVnd.Should().Be(expectedDelta);
            }

            ParcelClaimAppealResponse appealDecision;
            await using (var appealDecisionContext = CreateDbContext(dataSource))
            {
                appealDecision = await ExecuteAppealDecisionAsync(
                    appealDecisionContext,
                    new DecideParcelClaimAppealCommand(
                        appeal.Id,
                        operatorId,
                        operatorAdminId,
                        "APPROVE_ADJUSTMENT",
                        ParcelClaimProofStatus.VERIFIED.ToString(),
                        RevisedProvenDirectLossVnd: 300_000,
                        AcceptedEvidenceIds: [evidence.Id],
                        "Approved the documented adjustment."));
            }

            appealDecision.Status.Should().Be(ParcelClaimAppealStatus.ADJUSTMENT_APPROVED.ToString());
            appealDecision.ProofStatus.Should().Be(ParcelClaimProofStatus.VERIFIED.ToString());
            appealDecision.AcceptedEvidenceIds.Should().Equal(evidence.Id);
            appealDecision.RevisedTotalAwardVnd.Should().Be(300_000);
            appealDecision.SupplementaryAwardVnd.Should().Be(expectedDelta);

            await using (var assertionContext = CreateDbContext(dataSource))
            {
                var persistedClaim = await assertionContext.ParcelClaims
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == claim.Id);
                var persistedAppeal = await assertionContext.ParcelClaimAppeals
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == appeal.Id);
                var claimAudit = await assertionContext.ParcelClaimDecisionEvidence
                    .AsNoTracking()
                    .SingleOrDefaultAsync(item => item.ClaimId == claim.Id);
                var appealAudit = await assertionContext.ParcelClaimAppealDecisionEvidence
                    .AsNoTracking()
                    .SingleAsync(item => item.AppealId == appeal.Id);

                persistedClaim.ProofStatus.Should().Be(claimProof);
                persistedAppeal.ProofStatus.Should().Be(ParcelClaimProofStatus.VERIFIED);
                if (verified)
                {
                    claimAudit.Should().NotBeNull();
                    claimAudit!.EvidenceId.Should().Be(evidence.Id);
                    claimAudit.AcceptedByUserId.Should().Be(operatorAdminId);
                }
                else
                {
                    claimAudit.Should().BeNull();
                }
                persistedClaim.TotalAwardVnd.Should().Be(expectedOriginalAward);
                appealAudit.ClaimId.Should().Be(claim.Id);
                appealAudit.EvidenceId.Should().Be(evidence.Id);
                appealAudit.AcceptedByUserId.Should().Be(operatorAdminId);
                (await assertionContext.OutboxEvents.CountAsync(item =>
                    item.EventType == ParcelOutboxEvents.ParcelClaimDecided)).Should().Be(1);
                (await assertionContext.OutboxEvents.CountAsync(item =>
                    item.EventType == ParcelOutboxEvents.ParcelClaimAppealDecided)).Should().Be(1);

                var mutationAction = async () => await assertionContext.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE vietride_parcel.parcel_claim_appeal_decision_evidence
                    SET accepted_by_user_id = {Guid.NewGuid()}
                    WHERE id = {appealAudit.Id};
                    """);
                var immutable = await mutationAction.Should().ThrowAsync<PostgresException>();
                immutable.Which.SqlState.Should().Be(PostgresErrorCodes.ObjectNotInPrerequisiteState);
            }
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static ParcelEntity CreateParcel(Guid operatorId, Guid tripId, Guid senderId)
        => ParcelEntity.CreatePendingPayment(
            $"VRP-COMP-{Guid.NewGuid():N}"[..24],
            senderId,
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            "recipient@example.com",
            operatorId,
            tripId,
            Guid.NewGuid(),
            null,
            "Minimal compensation policy flow.",
            null,
            ParcelSizeCategory.SMALL,
            1m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(150_000));

    private static async Task<ParcelClaimResponse> ExecuteClaimDecisionAsync(
        ParcelDbContext dbContext,
        DecideParcelClaimCommand command)
    {
        var clock = new SystemClock();
        var handler = new DecideParcelClaimCommandHandler(
            CreateParcelRepository(dbContext),
            CreateReliabilityRepository(dbContext),
            new IntegrationEventOutbox(new OutboxStore(dbContext, clock)),
            clock);
        var behavior = new TransactionBehavior<DecideParcelClaimCommand, ParcelClaimResponse>(
            NullLogger<TransactionBehavior<DecideParcelClaimCommand, ParcelClaimResponse>>.Instance,
            new EfUnitOfWork(dbContext));
        return await behavior.Handle(
            command,
            () => handler.Handle(command, CancellationToken.None),
            CancellationToken.None);
    }

    private static async Task<ParcelClaimAppealResponse> ExecuteAppealDecisionAsync(
        ParcelDbContext dbContext,
        DecideParcelClaimAppealCommand command)
    {
        var clock = new SystemClock();
        var handler = new DecideParcelClaimAppealCommandHandler(
            CreateParcelRepository(dbContext),
            CreateReliabilityRepository(dbContext),
            new IntegrationEventOutbox(new OutboxStore(dbContext, clock)),
            clock);
        var behavior = new TransactionBehavior<DecideParcelClaimAppealCommand, ParcelClaimAppealResponse>(
            NullLogger<TransactionBehavior<DecideParcelClaimAppealCommand, ParcelClaimAppealResponse>>.Instance,
            new EfUnitOfWork(dbContext));
        return await behavior.Handle(
            command,
            () => handler.Handle(command, CancellationToken.None),
            CancellationToken.None);
    }

    private static IParcelRepository CreateParcelRepository(ParcelDbContext dbContext)
    {
        var repositoryType = typeof(ParcelDbContext).Assembly.GetType(
            "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelRepository",
            throwOnError: true)!;
        return (IParcelRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static IParcelReliabilityRepository CreateReliabilityRepository(ParcelDbContext dbContext)
    {
        var repositoryType = typeof(ParcelDbContext).Assembly.GetType(
            "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelReliabilityRepository",
            throwOnError: true)!;
        return (IParcelReliabilityRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        ParcelDbContext.ConfigurePostgresTypes(builder);
        builder.MapEnum<OutboxEventStatus>(
            $"{ParcelDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        return builder.Build();
    }

    private static ParcelDbContext CreateDbContext(NpgsqlDataSource dataSource)
        => new(ParcelIntegrationDbContextOptions.Create(dataSource), new SystemClock());

    private static string CreateConnectionString(string databaseName)
    {
        const string defaultConnectionString =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configuredConnectionString =
            Environment.GetEnvironmentVariable("VIETRIDE_PARCEL_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configuredConnectionString)
            ? defaultConnectionString
            : configuredConnectionString;

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
}
