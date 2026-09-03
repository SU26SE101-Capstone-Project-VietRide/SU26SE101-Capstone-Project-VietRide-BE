using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Reliability.Claims;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class SubmitParcelClaimEvidenceInheritanceTests
{
    [Fact]
    public async Task Handle_InheritsDistinctIncidentEvidenceIntoNewClaimWithoutAcceptingIt()
    {
        var now = new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);
        var senderId = Guid.NewGuid();
        var parcel = CreateParcel(senderId);
        var firstReference = "https://firebasestorage.googleapis.com/incident-one.jpg";
        var secondReference = "https://firebasestorage.googleapis.com/incident-two.jpg";
        var incident = CreateLostIncident(
            parcel,
            senderId,
            now,
            $$"""["{{firstReference}}"," {{firstReference}} ","","{{secondReference}}"]""");
        var reliability = CreateReliability(parcel, incident);
        var addedEvidence = new List<ParcelClaimEvidence>();
        reliability.When(repository => repository.AddClaimEvidenceAsync(
                Arg.Any<ParcelClaimEvidence>(),
                Arg.Any<CancellationToken>()))
            .Do(call => addedEvidence.Add(call.Arg<ParcelClaimEvidence>()));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var handler = new SubmitParcelClaimCommandHandler(
            CreateParcelRepository(parcel),
            reliability,
            CreateCustodyExceptionRepository(),
            Substitute.For<IIntegrationEventOutbox>(),
            clock);

        var response = await handler.Handle(
            new SubmitParcelClaimCommand(parcel.Id, senderId, null),
            CancellationToken.None);

        addedEvidence.Should().HaveCount(2);
        addedEvidence.Should().OnlyContain(evidence =>
            evidence.EvidenceType == ParcelClaimEvidence.IncidentPhotoEvidenceType
            && evidence.ClaimId == response.ClaimId
            && evidence.UploadedByUserId == senderId);
        addedEvidence.Select(evidence => evidence.Reference)
            .Should().Equal(firstReference, secondReference);
        response.Evidence.Select(evidence => evidence.Reference)
            .Should().Equal(firstReference, secondReference);
        response.AcceptedEvidenceIds.Should().BeEmpty();
        response.ProofStatus.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MalformedHistoricalIncidentEvidence_DoesNotBlockClaimSubmission()
    {
        var now = new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);
        var senderId = Guid.NewGuid();
        var parcel = CreateParcel(senderId);
        var incident = CreateLostIncident(parcel, senderId, now, "not-json");
        var reliability = CreateReliability(parcel, incident);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var handler = new SubmitParcelClaimCommandHandler(
            CreateParcelRepository(parcel),
            reliability,
            CreateCustodyExceptionRepository(),
            Substitute.For<IIntegrationEventOutbox>(),
            clock);

        var response = await handler.Handle(
            new SubmitParcelClaimCommand(parcel.Id, senderId, null),
            CancellationToken.None);

        response.Evidence.Should().BeEmpty();
        await reliability.DidNotReceive().AddClaimEvidenceAsync(
            Arg.Any<ParcelClaimEvidence>(),
            Arg.Any<CancellationToken>());
    }

    private static IParcelReliabilityRepository CreateReliability(
        ParcelEntity parcel,
        ParcelIncident incident)
    {
        var repository = Substitute.For<IParcelReliabilityRepository>();
        repository.ListIncidentsByParcelAsync(parcel.Id, Arg.Any<CancellationToken>())
            .Returns([incident]);
        repository.ListClaimEvidenceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelClaimEvidence>());
        repository.ListClaimDecisionEvidenceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelClaimDecisionEvidence>());
        return repository;
    }

    private static IParcelRepository CreateParcelRepository(ParcelEntity parcel)
    {
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);
        return repository;
    }

    private static IParcelCustodyExceptionRequestRepository CreateCustodyExceptionRepository()
    {
        var repository = Substitute.For<IParcelCustodyExceptionRequestRepository>();
        repository.GetByIncidentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ParcelCustodyExceptionRequest?)null);
        return repository;
    }

    private static ParcelEntity CreateParcel(Guid senderId)
        => ParcelEntity.CreatePendingPayment(
            "VR-CLAIM-EVIDENCE-001",
            senderId,
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("0900000000"),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Package",
            null,
            ParcelSizeCategory.SMALL,
            1m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(150_000));

    private static ParcelIncident CreateLostIncident(
        ParcelEntity parcel,
        Guid reporterId,
        DateTimeOffset now,
        string evidenceJson)
    {
        var incident = ParcelIncident.Open(
            parcel.Id,
            parcel.OperatorId,
            ParcelIncidentType.DAMAGED,
            now.AddHours(72),
            parcel.TripId,
            null,
            reporterId,
            "USER",
            "Destination",
            null,
            "Package was lost.",
            evidenceJson,
            operatorProcessBreach: false);
        incident.Escalate(now);
        incident.ExpireSearch();
        incident.ConfirmLost("Not recovered.", now);
        return incident;
    }
}
