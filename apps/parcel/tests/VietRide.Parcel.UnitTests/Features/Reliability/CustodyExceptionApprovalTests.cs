using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Reliability.Claims;
using VietRide.Parcel.Application.Features.Reliability.CustodyException;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class CustodyExceptionApprovalTests
{
    [Fact]
    public void AssistantRequest_DoesNotAcceptSupervisorUserId()
    {
        typeof(CustodyExceptionRequest).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain("SupervisorApprovalUserId");
    }

    [Fact]
    public void CrewController_ExposesDriverCustodyExceptionReadAndDecisionRoutes()
    {
        var read = typeof(VietRide.Parcel.Api.Controllers.CrewParcelsController)
            .GetMethod("GetCustodyExceptionAsync")!;
        var decision = typeof(VietRide.Parcel.Api.Controllers.CrewParcelsController)
            .GetMethod("DecideCustodyExceptionAsync")!;

        read.GetCustomAttributes(typeof(HttpMethodAttribute), inherit: true)
            .Cast<HttpMethodAttribute>()
            .Single().Template.Should().Be("{parcelId:guid}/custody-exception");
        decision.GetCustomAttributes(typeof(HttpMethodAttribute), inherit: true)
            .Cast<HttpMethodAttribute>()
            .Single().Template.Should().Be("{parcelId:guid}/custody-exception-decision");
    }

    [Fact]
    public async Task AssistantReport_OpensApprovalButDoesNotStartSearchBeforeCustodyApproval()
    {
        var now = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);
        var parcel = ParcelEntity.CreatePendingPayment(
            "VR-CUSTODY-REPORT-001",
            Guid.NewGuid(),
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
            2m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));
        var assistantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var parcels = Substitute.For<IParcelRepository>();
        parcels.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);
        parcels.TrySetPendingOperatorActionAsync(
                parcel.Id,
                PendingActionType.CUSTODY_EXCEPTION,
                Arg.Any<string>(),
                null,
                now,
                Arg.Any<CancellationToken>(),
                parcel.Status)
            .Returns(true);
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetOpenIncidentAsync(
                parcel.Id,
                ParcelIncidentType.WRONG_STOP,
                Arg.Any<CancellationToken>())
            .Returns((ParcelIncident?)null);
        reliability.GetLatestTransitLegAsync(parcel.Id, Arg.Any<CancellationToken>())
            .Returns((ParcelTransitLeg?)null);
        var requests = Substitute.For<IParcelCustodyExceptionRequestRepository>();
        ParcelCustodyExceptionRequest? addedRequest = null;
        requests.AddAsync(
                Arg.Do<ParcelCustodyExceptionRequest>(item => addedRequest = item),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var trips = Substitute.For<ITripServiceClient>();
        trips.AuthorizeAssistantForTripAsync(
                parcel.TripId,
                assistantId,
                parcel.OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        var response = await new ReportCustodyExceptionCommandHandler(
                parcels,
                reliability,
                requests,
                trips,
                clock)
            .Handle(
                new ReportCustodyExceptionCommand(
                    parcel.Id,
                    assistantId,
                    parcel.OperatorId,
                    "ASSISTANT",
                    "WRONG_STOP",
                    "ROUTE_STOP",
                    Guid.NewGuid(),
                    "Wrong station",
                    null,
                    "Package was unloaded outside normal scan flow.",
                    2m,
                    ["photo-1"],
                    "Physical wrong-stop unload.",
                    idempotencyKey),
                CancellationToken.None);

        response.Status.Should().Be("PENDING_APPROVAL");
        response.SearchDeadline.Should().BeNull();
        response.ApprovedCustodyEventId.Should().BeNull();
        response.AvailableActions.Should().ContainSingle().Which.Should().Be("WAIT_FOR_APPROVAL");
        addedRequest.Should().NotBeNull();
        addedRequest!.ReportedByUserId.Should().Be(assistantId);
        addedRequest.IdempotencyKey.Should().Be(idempotencyKey);
        await reliability.DidNotReceiveWithAnyArgs().AddCustodyEventAsync(default!, default);
        await reliability.Received(1).AddIncidentAsync(
            Arg.Is<ParcelIncident>(item => item.Status == ParcelIncidentStatus.OPEN
                && item.SearchDeadline == null),
            Arg.Any<CancellationToken>());
        await reliability.DidNotReceiveWithAnyArgs().AddSearchTaskAsync(default!, default);
    }

    [Fact]
    public async Task AssignedDriverApproval_UsesJwtReviewerAndCreatesCustodyFact()
    {
        var fixture = CreateFixture();
        var driverId = Guid.NewGuid();
        var custodyEvent = CreateCustodyEvent(fixture.Parcel, fixture.Request, driverId);
        fixture.Trips.AuthorizeCrewForTripAsync(
                fixture.Parcel.TripId,
                driverId,
                fixture.Parcel.OperatorId,
                "DRIVER",
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        fixture.Custody.AppendAsync(
                fixture.Parcel,
                ParcelCustodyEventType.MANUAL_CUSTODY_EXCEPTION,
                fixture.Request.ActualLocationType,
                fixture.Request.ActualLocationId,
                fixture.Request.LocationSnapshot,
                fixture.Request.ReportedByUserId,
                fixture.Request.ReportedByRole,
                "APPROVED_CUSTODY_EXCEPTION",
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                fixture.Request.Reason,
                Arg.Any<CancellationToken>())
            .Returns(custodyEvent);

        var response = await fixture.Handler.Handle(
            new DecideCustodyExceptionCommand(
                fixture.Parcel.Id,
                "PARCEL",
                driverId,
                fixture.Parcel.OperatorId,
                "DRIVER",
                "APPROVE",
                "Evidence confirmed.",
                Guid.NewGuid()),
            CancellationToken.None);

        response.Status.Should().Be("APPROVED");
        response.ApprovedCustodyEventId.Should().Be(custodyEvent.Id);
        fixture.Request.ReviewedByUserId.Should().Be(driverId);
        fixture.Request.ReviewedByRole.Should().Be("DRIVER");
        fixture.Incident.OperatorProcessBreach.Should().BeTrue();
        fixture.Incident.Status.Should().Be(ParcelIncidentStatus.SEARCHING);
        fixture.Incident.SearchDeadline.Should().Be(fixture.Now.AddHours(72));
        await fixture.Reliability.Received(2).AddSearchTaskAsync(
            Arg.Any<ParcelSearchTask>(),
            Arg.Any<CancellationToken>());
        await fixture.Outbox.Received(1).EnqueueAsync(
            ParcelOutboxEvents.IncidentOpened,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await fixture.Requests.Received(1).GetLatestByParcelForUpdateAsync(
            fixture.Parcel.Id,
            Arg.Any<CancellationToken>());
        await fixture.Trips.Received(1).AuthorizeCrewForTripAsync(
            fixture.Parcel.TripId,
            driverId,
            fixture.Parcel.OperatorId,
            "DRIVER",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameTenantOperatorReject_ClosesSearchAndRestoresParcelWithoutCustodyFact()
    {
        var fixture = CreateFixture();
        var reviewerId = Guid.NewGuid();
        var task = ParcelSearchTask.Create(
            fixture.Incident.Id,
            fixture.Parcel.Id,
            ParcelSearchTaskType.VEHICLE_SWEEP,
            null,
            null,
            fixture.Now.AddMinutes(30));
        fixture.Reliability.ListSearchTasksAsync(
                fixture.Incident.Id,
                Arg.Any<CancellationToken>())
            .Returns([task]);
        fixture.Parcels.TryResolvePendingOperatorActionAsync(
                fixture.Parcel.Id,
                PendingActionType.CUSTODY_EXCEPTION,
                fixture.Now,
                Arg.Any<CancellationToken>())
            .Returns(new ParcelPaymentTransitionSnapshot(
                fixture.Parcel.Id,
                fixture.Parcel.ParcelCode,
                ParcelStatus.IN_TRANSIT,
                0,
                0,
                fixture.Parcel.OperatorId,
                fixture.Parcel.TripId,
                fixture.Parcel.BookingId,
                fixture.Parcel.SenderUserId,
                fixture.Parcel.SizeCategory,
                null));

        var response = await fixture.Handler.Handle(
            new DecideCustodyExceptionCommand(
                fixture.Incident.Id,
                "INCIDENT",
                reviewerId,
                fixture.Parcel.OperatorId,
                "OPERATOR_STAFF",
                "REJECT",
                "Station CCTV disproved the report.",
                Guid.NewGuid()),
            CancellationToken.None);

        response.Status.Should().Be("REJECTED");
        response.IncidentStatus.Should().Be("RESOLVED");
        fixture.Incident.ResolutionCode.Should().Be("SUPERVISOR_REJECTED");
        fixture.Request.ReviewedByUserId.Should().Be(reviewerId);
        task.Status.Should().Be(ParcelSearchTaskStatus.CANCELLED);
        await fixture.Custody.DidNotReceiveWithAnyArgs().AppendAsync(
            default!, default, default, default, default, default, default!, default!, default,
            default, default, default);
        await fixture.Trips.DidNotReceiveWithAnyArgs().AuthorizeCrewForTripAsync(
            default, default, default, default!, default);
    }

    [Fact]
    public async Task DecidedRequest_CannotBeReviewedAgain()
    {
        var fixture = CreateFixture();
        fixture.Request.Reject(Guid.NewGuid(), "OPERATOR_ADMIN", null, fixture.Now);

        var action = () => fixture.Handler.Handle(
            new DecideCustodyExceptionCommand(
                fixture.Incident.Id,
                "INCIDENT",
                Guid.NewGuid(),
                fixture.Parcel.OperatorId,
                "OPERATOR_ADMIN",
                "APPROVE",
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        await action.Should().ThrowAsync<VietRide.Shared.Application.Exceptions.CodedConflictException>()
            .WithMessage("*already been decided*");
    }

    [Fact]
    public async Task UnassignedDriver_CannotApproveReport()
    {
        var fixture = CreateFixture();
        var driverId = Guid.NewGuid();
        fixture.Trips.AuthorizeCrewForTripAsync(
                fixture.Parcel.TripId,
                driverId,
                fixture.Parcel.OperatorId,
                "DRIVER",
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Denied));

        var action = () => fixture.Handler.Handle(
            new DecideCustodyExceptionCommand(
                fixture.Parcel.Id,
                "PARCEL",
                driverId,
                fixture.Parcel.OperatorId,
                "DRIVER",
                "APPROVE",
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        await action.Should().ThrowAsync<VietRide.Shared.Application.Exceptions.ForbiddenException>();
        await fixture.Custody.DidNotReceiveWithAnyArgs().AppendAsync(
            default!, default, default, default, default, default, default!, default!, default,
            default, default, default);
    }

    [Fact]
    public async Task CrossTenantOperator_CannotDiscoverOrDecideReport()
    {
        var fixture = CreateFixture();

        var action = () => fixture.Handler.Handle(
            new DecideCustodyExceptionCommand(
                fixture.Incident.Id,
                "INCIDENT",
                Guid.NewGuid(),
                Guid.NewGuid(),
                "OPERATOR_ADMIN",
                "REJECT",
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        await action.Should()
            .ThrowAsync<VietRide.Shared.Application.Exceptions.CodedNotFoundException>()
            .WithMessage("*was not found*");
        await fixture.Custody.DidNotReceiveWithAnyArgs().AppendAsync(
            default!, default, default, default, default, default, default!, default!, default,
            default, default, default);
    }

    [Fact]
    public async Task PendingApproval_BlocksFoundAndLostMutations()
    {
        var fixture = CreateFixture();
        fixture.Requests.GetByIncidentAsync(fixture.Incident.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.Request);

        var markFound = () => new MarkIncidentFoundCommandHandler(
                fixture.Reliability,
                fixture.Parcels,
                fixture.Requests,
                fixture.Custody,
                Substitute.For<IIntegrationEventOutbox>(),
                fixture.Clock)
            .Handle(
                new MarkIncidentFoundCommand(
                    fixture.Incident.Id,
                    fixture.Parcel.OperatorId,
                    Guid.NewGuid(),
                    "VEHICLE",
                    null,
                    "Vehicle",
                    null,
                    "Found."),
                CancellationToken.None);
        var declareLost = () => new DeclareIncidentLostCommandHandler(
                fixture.Reliability,
                fixture.Requests,
                Substitute.For<IIntegrationEventOutbox>(),
                fixture.Clock)
            .Handle(
                new DeclareIncidentLostCommand(
                    fixture.Incident.Id,
                    fixture.Parcel.OperatorId,
                    Guid.NewGuid(),
                    "Not found."),
                CancellationToken.None);

        (await markFound.Should().ThrowAsync<VietRide.Shared.Application.Exceptions.CodedConflictException>())
            .Which.ErrorCode.Should().Be("PARCEL_CUSTODY_EXCEPTION_APPROVAL_REQUIRED");
        (await declareLost.Should().ThrowAsync<VietRide.Shared.Application.Exceptions.CodedConflictException>())
            .Which.ErrorCode.Should().Be("PARCEL_CUSTODY_EXCEPTION_APPROVAL_REQUIRED");
    }

    [Fact]
    public async Task PendingApproval_BlocksClaimEvenIfIncidentWasIncorrectlyMarkedLost()
    {
        var fixture = CreateFixture();
        fixture.Incident.Escalate(fixture.Now);
        fixture.Incident.ExpireSearch();
        fixture.Incident.ConfirmLost("Legacy inconsistent state.", fixture.Now);
        fixture.Reliability.ListIncidentsByParcelAsync(
                fixture.Parcel.Id,
                Arg.Any<CancellationToken>())
            .Returns([fixture.Incident]);
        fixture.Requests.GetByIncidentAsync(fixture.Incident.Id, Arg.Any<CancellationToken>())
            .Returns(fixture.Request);

        var action = () => new SubmitParcelClaimCommandHandler(
                fixture.Parcels,
                fixture.Reliability,
                fixture.Requests,
                Substitute.For<IIntegrationEventOutbox>(),
                fixture.Clock)
            .Handle(
                new SubmitParcelClaimCommand(
                    fixture.Parcel.Id,
                    fixture.Parcel.SenderUserId,
                    null),
                CancellationToken.None);

        (await action.Should().ThrowAsync<VietRide.Shared.Application.Exceptions.CodedConflictException>())
            .Which.ErrorCode.Should().Be("PARCEL_CUSTODY_EXCEPTION_APPROVAL_REQUIRED");
    }

    private static Fixture CreateFixture()
    {
        var now = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);
        var parcel = ParcelEntity.CreatePendingPayment(
            "VR-CUSTODY-APPROVAL-001",
            Guid.NewGuid(),
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
            2m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));
        var incident = ParcelIncident.Open(
            parcel.Id,
            parcel.OperatorId,
            ParcelIncidentType.WRONG_STOP,
            now.AddHours(72),
            parcel.TripId,
            null,
            Guid.NewGuid(),
            "ASSISTANT",
            "STOP:EXPECTED",
            "Wrong station",
            "Package was physically unloaded.",
            null,
            operatorProcessBreach: false);
        var request = ParcelCustodyExceptionRequest.Create(
            parcel.Id,
            incident.Id,
            parcel.OperatorId,
            parcel.TripId,
            ParcelIncidentType.WRONG_STOP,
            ParcelCustodyLocationType.ROUTE_STOP,
            Guid.NewGuid(),
            "Wrong station",
            null,
            "Package was physically unloaded.",
            2m,
            "[\"photo-1\"]",
            "Wrong-stop unload outside the normal scan flow.",
            incident.ReporterId!.Value,
            "ASSISTANT",
            now,
            Guid.NewGuid());

        var requests = Substitute.For<IParcelCustodyExceptionRequestRepository>();
        requests.GetLatestByParcelForUpdateAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(request);
        requests.GetByIncidentForUpdateAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(request);
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetIncidentAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);
        reliability.ListSearchTasksAsync(incident.Id, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelSearchTask>());
        var parcels = Substitute.For<IParcelRepository>();
        parcels.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);
        var custody = Substitute.For<IParcelCustodyService>();
        var trips = Substitute.For<ITripServiceClient>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        outbox.EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var handler = new DecideCustodyExceptionCommandHandler(
            requests,
            reliability,
            parcels,
            custody,
            trips,
            outbox,
            clock);
        return new Fixture(now, parcel, incident, request, requests, reliability, parcels, custody, trips, outbox, clock, handler);
    }

    private static ParcelCustodyEvent CreateCustodyEvent(
        ParcelEntity parcel,
        ParcelCustodyExceptionRequest request,
        Guid actorId)
        => ParcelCustodyEvent.Create(
            parcel.Id,
            null,
            parcel.TripId,
            ParcelCustodyEventType.MANUAL_CUSTODY_EXCEPTION,
            null,
            null,
            request.ActualLocationType,
            request.ActualLocationId,
            request.LocationSnapshot,
            null,
            actorId,
            "ASSISTANT",
            request.ReportedAt,
            "APPROVED_CUSTODY_EXCEPTION",
            Guid.NewGuid().ToString("D"),
            request.EvidenceReferencesJson,
            request.Reason,
            1);

    private sealed record Fixture(
        DateTimeOffset Now,
        ParcelEntity Parcel,
        ParcelIncident Incident,
        ParcelCustodyExceptionRequest Request,
        IParcelCustodyExceptionRequestRepository Requests,
        IParcelReliabilityRepository Reliability,
        IParcelRepository Parcels,
        IParcelCustodyService Custody,
        ITripServiceClient Trips,
        IIntegrationEventOutbox Outbox,
        IClock Clock,
        DecideCustodyExceptionCommandHandler Handler);
}
