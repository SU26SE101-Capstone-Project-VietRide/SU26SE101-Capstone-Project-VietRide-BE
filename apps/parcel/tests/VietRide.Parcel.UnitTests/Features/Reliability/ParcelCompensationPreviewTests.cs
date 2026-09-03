using System.Reflection;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Reliability.Claims;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class ParcelCompensationPreviewTests
{
    [Fact]
    public async Task ClaimPreview_VerifiedProof_ReturnsAuthoritativeBreakdown()
    {
        var fixture = CreateFixture(declaredValueVnd: 300_000, freightVnd: 150_000);
        var evidence = ParcelClaimEvidence.Create(
            fixture.Claim.Id,
            "INVOICE",
            "invoice://accepted",
            null,
            Guid.NewGuid());
        fixture.Reliability.ListClaimEvidenceAsync(fixture.Claim.Id, Arg.Any<CancellationToken>())
            .Returns([evidence]);

        var response = await fixture.Handler.Handle(
            new PreviewParcelClaimAwardQuery(
                fixture.Claim.Id,
                fixture.Claim.OperatorId,
                "VERIFIED",
                300_000,
                [evidence.Id]),
            CancellationToken.None);

        response.CalculationBasis.Should().Be("VERIFIED_LOSS");
        response.AcceptedEvidenceIds.Should().Equal(evidence.Id);
        response.AssessedLossVnd.Should().Be(300_000);
        response.DeclaredLiabilityVnd.Should().Be(150_000);
        response.CargoAwardVnd.Should().Be(150_000);
        response.FreightRefundVnd.Should().Be(150_000);
        response.TotalAwardVnd.Should().Be(300_000);
    }

    [Theory]
    [InlineData("VERIFIED", null, false)]
    [InlineData("VERIFIED", 100_000L, false)]
    [InlineData("UNVERIFIED", 100_000L, false)]
    [InlineData("NO_PROOF", null, true)]
    public async Task ClaimPreview_InvalidProofMatrix_ReturnsEvidenceRequired(
        string proofStatus,
        long? loss,
        bool includeEvidence)
    {
        var fixture = CreateFixture();
        var evidenceId = Guid.NewGuid();
        var acceptedEvidenceIds = includeEvidence ? new[] { evidenceId } : Array.Empty<Guid>();

        var action = () => fixture.Handler.Handle(
            new PreviewParcelClaimAwardQuery(
                fixture.Claim.Id,
                fixture.Claim.OperatorId,
                proofStatus,
                loss,
                acceptedEvidenceIds),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedValidationException>()).Which;
        exception.ErrorCode.Should().Be("PARCEL_CLAIM_EVIDENCE_REQUIRED");
    }

    [Fact]
    public async Task ClaimPreview_DuplicateEvidenceIds_ReturnsEvidenceRequired()
    {
        var fixture = CreateFixture();
        var evidenceId = Guid.NewGuid();

        var action = () => fixture.Handler.Handle(
            new PreviewParcelClaimAwardQuery(
                fixture.Claim.Id,
                fixture.Claim.OperatorId,
                "VERIFIED",
                100_000,
                [evidenceId, evidenceId]),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedValidationException>()).Which;
        exception.ErrorCode.Should().Be("PARCEL_CLAIM_EVIDENCE_REQUIRED");
    }

    [Fact]
    public async Task ClaimPreview_EvidenceFromAnotherClaim_IsTenantMaskedNotFound()
    {
        var fixture = CreateFixture();
        var otherEvidence = ParcelClaimEvidence.Create(
            Guid.NewGuid(),
            "INVOICE",
            "invoice://other-claim",
            null,
            Guid.NewGuid());
        fixture.Reliability.ListClaimEvidenceAsync(fixture.Claim.Id, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelClaimEvidence>());

        var action = () => fixture.Handler.Handle(
            new PreviewParcelClaimAwardQuery(
                fixture.Claim.Id,
                fixture.Claim.OperatorId,
                "VERIFIED",
                100_000,
                [otherEvidence.Id]),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedNotFoundException>()).Which;
        exception.ErrorCode.Should().Be("PARCEL_CLAIM_EVIDENCE_NOT_FOUND");
    }

    [Fact]
    public async Task ClaimPreview_ForeignOperator_IsTenantMaskedBeforeEvidenceLookup()
    {
        var fixture = CreateFixture();

        var action = () => fixture.Handler.Handle(
            new PreviewParcelClaimAwardQuery(
                fixture.Claim.Id,
                Guid.NewGuid(),
                "NO_PROOF",
                null,
                []),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedNotFoundException>()).Which;
        exception.ErrorCode.Should().Be("PARCEL_CLAIM_NOT_FOUND");
        await fixture.Reliability.DidNotReceive().ListClaimEvidenceAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClaimDecision_PersistsProofEvidenceAndOutboxInTheHandlerUnitOfWork()
    {
        var fixture = CreateFixture(declaredValueVnd: 300_000, freightVnd: 150_000);
        var evidence = ParcelClaimEvidence.Create(
            fixture.Claim.Id,
            "INVOICE",
            "invoice://accepted",
            null,
            Guid.NewGuid());
        fixture.Reliability.GetClaimByIdForUpdateAsync(
                fixture.Claim.Id,
                Arg.Any<CancellationToken>())
            .Returns(fixture.Claim);
        fixture.Reliability.ListClaimEvidenceAsync(
                fixture.Claim.Id,
                Arg.Any<CancellationToken>())
            .Returns([evidence]);
        fixture.Reliability.ListClaimDecisionEvidenceAsync(
                fixture.Claim.Id,
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelClaimDecisionEvidence>());
        fixture.Reliability.GetClaimAppealByClaimAsync(
                fixture.Claim.Id,
                Arg.Any<CancellationToken>())
            .Returns((ParcelClaimAppeal?)null);
        fixture.Reliability.GetIncidentAsync(
                fixture.Claim.IncidentId,
                Arg.Any<CancellationToken>())
            .Returns((ParcelIncident?)null);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var now = DateTimeOffset.UtcNow;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var reviewerId = Guid.NewGuid();
        var handler = new DecideParcelClaimCommandHandler(
            fixture.Parcels,
            fixture.Reliability,
            outbox,
            clock);

        var response = await handler.Handle(
            new DecideParcelClaimCommand(
                fixture.Claim.Id,
                fixture.Claim.OperatorId,
                reviewerId,
                "APPROVE",
                "VERIFIED",
                300_000,
                [evidence.Id],
                "Accepted invoice."),
            CancellationToken.None);

        fixture.Claim.ProofStatus.Should().Be(ParcelClaimProofStatus.VERIFIED);
        response.ProofStatus.Should().Be("VERIFIED");
        response.AcceptedEvidenceIds.Should().Equal(evidence.Id);
        response.TotalAwardVnd.Should().Be(300_000);
        await fixture.Reliability.Received(1).AddClaimDecisionEvidenceAsync(
            Arg.Is<ParcelClaimDecisionEvidence>(link =>
                link.ClaimId == fixture.Claim.Id
                && link.EvidenceId == evidence.Id
                && link.AcceptedByUserId == reviewerId
                && link.AcceptedAt == now),
            Arg.Any<CancellationToken>());
        await fixture.Reliability.Received(1).UpdateClaimAsync(
            fixture.Claim,
            Arg.Any<CancellationToken>());
        await outbox.Received(1).EnqueueAsync(
            Arg.Is<Guid>(id => id != Guid.Empty),
            "parcel.claim.decided",
            Arg.Is<string>(payload => payload.Contains(fixture.Claim.Id.ToString(), StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppealPreview_SubtractsOriginalAwardAndDoesNotRefundFreightTwice()
    {
        var fixture = CreateFixture(declaredValueVnd: 300_000, freightVnd: 150_000);
        MarkClaimPaid(fixture.Claim, provenLossVnd: 100_000);
        var appeal = ParcelClaimAppeal.Submit(
            fixture.Claim,
            "Additional evidence.",
            fixture.Claim.BeneficiaryUserId,
            DateTimeOffset.UtcNow,
            Guid.NewGuid());
        var evidence = ParcelClaimEvidence.Create(
            fixture.Claim.Id,
            "INVOICE",
            "invoice://appeal",
            null,
            Guid.NewGuid());
        fixture.Reliability.GetClaimAppealByIdAsync(appeal.Id, Arg.Any<CancellationToken>())
            .Returns(appeal);
        fixture.Reliability.ListClaimEvidenceAsync(fixture.Claim.Id, Arg.Any<CancellationToken>())
            .Returns([evidence]);
        var handler = new PreviewParcelClaimAppealAdjustmentQueryHandler(
            fixture.Parcels,
            fixture.Reliability);

        var response = await handler.Handle(
            new PreviewParcelClaimAppealAdjustmentQuery(
                appeal.Id,
                fixture.Claim.OperatorId,
                "VERIFIED",
                300_000,
                [evidence.Id]),
            CancellationToken.None);

        response.OriginalTotalAwardVnd.Should().Be(200_000);
        response.CargoAwardVnd.Should().Be(150_000);
        response.FreightRefundVnd.Should().Be(150_000);
        response.TotalAwardVnd.Should().Be(300_000);
        response.SupplementaryAwardVnd.Should().Be(100_000);
    }

    [Fact]
    public async Task AppealDecision_PersistsAcceptedEvidenceAndPositiveDeltaOutbox()
    {
        var fixture = CreateFixture(declaredValueVnd: 300_000, freightVnd: 150_000);
        MarkClaimPaid(fixture.Claim, provenLossVnd: 100_000);
        var appeal = ParcelClaimAppeal.Submit(
            fixture.Claim,
            "Additional evidence.",
            fixture.Claim.BeneficiaryUserId,
            DateTimeOffset.UtcNow,
            Guid.NewGuid());
        var evidence = ParcelClaimEvidence.Create(
            fixture.Claim.Id,
            "INVOICE",
            "invoice://appeal",
            null,
            Guid.NewGuid());
        fixture.Reliability.GetClaimAppealByIdForUpdateAsync(
                appeal.Id,
                Arg.Any<CancellationToken>())
            .Returns(appeal);
        fixture.Reliability.ListClaimEvidenceAsync(fixture.Claim.Id, Arg.Any<CancellationToken>())
            .Returns([evidence]);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var now = DateTimeOffset.UtcNow;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var reviewerId = Guid.NewGuid();
        var handler = new DecideParcelClaimAppealCommandHandler(
            fixture.Parcels,
            fixture.Reliability,
            outbox,
            clock);

        var response = await handler.Handle(
            new DecideParcelClaimAppealCommand(
                appeal.Id,
                fixture.Claim.OperatorId,
                reviewerId,
                "APPROVE_ADJUSTMENT",
                "VERIFIED",
                300_000,
                [evidence.Id],
                "Accepted additional evidence."),
            CancellationToken.None);

        response.ProofStatus.Should().Be("VERIFIED");
        response.AcceptedEvidenceIds.Should().Equal(evidence.Id);
        response.SupplementaryAwardVnd.Should().Be(100_000);
        await fixture.Reliability.Received(1).AddClaimAppealDecisionEvidenceAsync(
            Arg.Is<ParcelClaimAppealDecisionEvidence>(link =>
                link.AppealId == appeal.Id
                && link.ClaimId == fixture.Claim.Id
                && link.EvidenceId == evidence.Id
                && link.AcceptedByUserId == reviewerId
                && link.AcceptedAt == now),
            Arg.Any<CancellationToken>());
        await fixture.Reliability.Received(1).UpdateClaimAppealAsync(
            appeal,
            Arg.Any<CancellationToken>());
        await outbox.Received(1).EnqueueAsync(
            Arg.Is<Guid>(id => id != Guid.Empty),
            "parcel.claim_appeal.decided",
            Arg.Is<string>(payload =>
                payload.Contains(appeal.Id.ToString(), StringComparison.Ordinal)
                && payload.Contains("100000", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    private static PreviewFixture CreateFixture(
        long? declaredValueVnd = 1_000_000,
        long freightVnd = 100_000)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-PREVIEW-001",
            Guid.NewGuid(),
            null,
            "Recipient",
            PhoneNumber.Normalize("0900000000"),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            "Package",
            null,
            ParcelSizeCategory.SMALL,
            1m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(freightVnd));
        parcel.AcceptDeclaration(declaredValueVnd, 1, DateTimeOffset.UtcNow);
        SetPrivateProperty(parcel, nameof(ParcelEntity.FinalTotalPriceVnd), Money.FromRaw(freightVnd));

        var claim = ParcelClaim.Submit(
            parcel.Id,
            Guid.NewGuid(),
            parcel.OperatorId,
            parcel.SenderUserId,
            declaredValueVnd,
            1,
            50,
            30_000_000,
            4);
        var parcels = Substitute.For<IParcelRepository>();
        parcels.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetClaimByIdAsync(claim.Id, Arg.Any<CancellationToken>()).Returns(claim);

        return new PreviewFixture(
            parcel,
            claim,
            parcels,
            reliability,
            new PreviewParcelClaimAwardQueryHandler(parcels, reliability));
    }

    private static void SetPrivateProperty<T>(ParcelEntity parcel, string propertyName, T value)
        => typeof(ParcelEntity)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(parcel, value);

    private static void MarkClaimPaid(ParcelClaim claim, long provenLossVnd)
    {
        claim.BeginReview();
        claim.Approve(
            ParcelClaimProofStatus.VERIFIED,
            provenLossVnd,
            50,
            30_000_000,
            provenLossVnd / 2,
            150_000,
            "Approved.",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        claim.MarkPaid(Guid.NewGuid(), DateTimeOffset.UtcNow);
    }

    private sealed record PreviewFixture(
        ParcelEntity Parcel,
        ParcelClaim Claim,
        IParcelRepository Parcels,
        IParcelReliabilityRepository Reliability,
        PreviewParcelClaimAwardQueryHandler Handler);
}
