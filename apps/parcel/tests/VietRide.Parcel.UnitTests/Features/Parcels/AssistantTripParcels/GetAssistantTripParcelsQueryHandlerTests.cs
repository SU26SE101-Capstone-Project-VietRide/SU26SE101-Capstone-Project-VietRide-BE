using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Parcels.AssistantTripParcels;

public sealed class GetAssistantTripParcelsQueryHandlerTests
{
    private const string PhotoUrl = "https://storage.googleapis.com/vietride.appspot.com/parcels/photo.jpg";
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();

    [Fact]
    public async Task Handle_AuthorizedAssistant_ReturnsMappedPagedParcels()
    {
        var parcel = CreateParcel();
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var screenModels = Substitute.For<IParcelReliabilityReadModelService>();
        tripClient.AuthorizeAssistantForTripAsync(TripId, UserId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        repository.ListByTripAndOperatorFilteredAsync(
                TripId,
                OperatorId,
                null,
                null,
                null,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        repository.GetAssistantManifestCountsAsync(TripId, OperatorId, null, Arg.Any<CancellationToken>())
            .Returns(new AssistantParcelManifestCounts(1, 0, 0, 0, 0, 0, 0));
        screenModels.BuildAsync(
                Arg.Any<IReadOnlyCollection<ParcelEntity>>(),
                UserId,
                false,
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ParcelScreenReadModel>
            {
                [parcel.Id] = CreateScreen(parcel),
            });

        var result = await new GetAssistantTripParcelsQueryHandler(
                repository, tripClient, screenModels, Substitute.For<IParcelCustodyExceptionRequestRepository>())
            .Handle(new GetAssistantTripParcelsQuery(TripId, UserId, OperatorId, 1, 20), default);

        result.Items.Should().ContainSingle();
        result.Items[0].AvailableActions.Should().Contain("REWEIGH");
        result.Items[0].AvailableActions.Should().NotContain("CHECK_IN");
        result.Items[0].AvailableActions.Should().NotContain("CUSTODY_SCAN");
        result.Items[0].Should().BeEquivalentTo(new
        {
            ParcelId = parcel.Id,
            parcel.ParcelCode,
            Status = parcel.Status.ToString(),
            parcel.RecipientName,
            RecipientPhone = parcel.RecipientPhone.ToString(),
            parcel.DropoffStopId,
            SizeCategory = parcel.SizeCategory.ToString(),
            EstimatedSizeCategory = parcel.EstimatedSizeCategory.ToString(),
            ActualSizeCategory = parcel.ActualSizeCategory?.ToString(),
            parcel.EstimatedWeightKg,
            parcel.ActualWeightKg,
            BalanceRequiredVnd = parcel.BalanceRequiredVnd.Amount,
            BalancePaidVnd = parcel.BalancePaidVnd.Amount,
            parcel.FinalPaymentDeadline,
            parcel.Description,
            parcel.PhotoUrl,
        });
        await repository.Received(1).ListByTripAndOperatorFilteredAsync(
            TripId,
            OperatorId,
            null,
            null,
            null,
            null,
            1,
            20,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeniedAssistant_DoesNotQueryParcels()
    {
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var screenModels = Substitute.For<IParcelReliabilityReadModelService>();
        tripClient.AuthorizeAssistantForTripAsync(TripId, UserId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Denied));

        var action = () => new GetAssistantTripParcelsQueryHandler(
                repository, tripClient, screenModels, Substitute.For<IParcelCustodyExceptionRequestRepository>())
            .Handle(new GetAssistantTripParcelsQuery(TripId, UserId, OperatorId, 1, 20), default);

        await action.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.ErrorCode == "FORBIDDEN");
        await repository.DidNotReceive().ListByTripAndOperatorFilteredAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<ParcelStatus?>(),
            Arg.Any<bool?>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_IncomingTransferParcel_IsMarkedTransferInForReplacementCrew()
    {
        var sourceTripId = Guid.NewGuid();
        var parcel = CreateParcel();
        typeof(ParcelEntity).GetProperty(nameof(ParcelEntity.TripId))!.SetValue(parcel, sourceTripId);
        typeof(ParcelEntity).GetProperty(nameof(ParcelEntity.TransferTargetTripId))!.SetValue(parcel, TripId);
        typeof(ParcelEntity).GetProperty(nameof(ParcelEntity.Status))!.SetValue(parcel, ParcelStatus.PENDING_TRANSFER_CONFIRM);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var screenModels = Substitute.For<IParcelReliabilityReadModelService>();
        tripClient.AuthorizeAssistantForTripAsync(TripId, UserId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        repository.ListByTripAndOperatorFilteredAsync(
                TripId, OperatorId, null, null, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        repository.GetAssistantManifestCountsAsync(TripId, OperatorId, null, Arg.Any<CancellationToken>())
            .Returns(new AssistantParcelManifestCounts(1, 0, 0, 0, 0, 0, 0));
        screenModels.BuildAsync(Arg.Any<IReadOnlyCollection<ParcelEntity>>(), UserId, false, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ParcelScreenReadModel> { [parcel.Id] = CreateScreen(parcel) });

        var result = await new GetAssistantTripParcelsQueryHandler(
                repository, tripClient, screenModels, Substitute.For<IParcelCustodyExceptionRequestRepository>())
            .Handle(new GetAssistantTripParcelsQuery(TripId, UserId, OperatorId, 1, 20), default);

        result.Items.Should().ContainSingle();
        result.Items[0].TransferContext.Should().Be("TRANSFER_IN");
        result.Items[0].SourceTripId.Should().Be(sourceTripId);
        result.Items[0].TargetTripId.Should().Be(TripId);
        result.Items[0].AvailableActions.Should().Contain("CONFIRM_TRANSFER");
    }

    [Fact]
    public async Task Handle_OutgoingTransferParcel_DoesNotOfferTargetCrewConfirmation()
    {
        var parcel = CreateParcel();
        typeof(ParcelEntity).GetProperty(nameof(ParcelEntity.TripId))!.SetValue(parcel, TripId);
        typeof(ParcelEntity).GetProperty(nameof(ParcelEntity.TransferTargetTripId))!.SetValue(parcel, Guid.NewGuid());
        typeof(ParcelEntity).GetProperty(nameof(ParcelEntity.Status))!.SetValue(parcel, ParcelStatus.PENDING_TRANSFER_CONFIRM);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var screenModels = Substitute.For<IParcelReliabilityReadModelService>();
        tripClient.AuthorizeAssistantForTripAsync(TripId, UserId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        repository.ListByTripAndOperatorFilteredAsync(
                TripId, OperatorId, null, null, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        repository.GetAssistantManifestCountsAsync(TripId, OperatorId, null, Arg.Any<CancellationToken>())
            .Returns(new AssistantParcelManifestCounts(1, 0, 0, 0, 0, 0, 0));
        screenModels.BuildAsync(Arg.Any<IReadOnlyCollection<ParcelEntity>>(), UserId, false, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ParcelScreenReadModel> { [parcel.Id] = CreateScreen(parcel) });

        var result = await new GetAssistantTripParcelsQueryHandler(
                repository, tripClient, screenModels, Substitute.For<IParcelCustodyExceptionRequestRepository>())
            .Handle(new GetAssistantTripParcelsQuery(TripId, UserId, OperatorId, 1, 20), default);

        result.Items.Should().ContainSingle();
        result.Items[0].TransferContext.Should().BeNull();
        result.Items[0].AvailableActions.Should().NotContain("CONFIRM_TRANSFER");
    }

    [Fact]
    public async Task Handle_AssignedDriver_UsesCrewAuthorizationForSharedManifest()
    {
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var screenModels = Substitute.For<IParcelReliabilityReadModelService>();
        tripClient.AuthorizeCrewForTripAsync(
                TripId, UserId, OperatorId, "DRIVER", Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        repository.ListByTripAndOperatorFilteredAsync(
                TripId, OperatorId, null, null, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([], 1, 20, 0));
        repository.GetAssistantManifestCountsAsync(TripId, OperatorId, null, Arg.Any<CancellationToken>())
            .Returns(new AssistantParcelManifestCounts(0, 0, 0, 0, 0, 0, 0));
        tripClient.GetTripSummariesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.Success([CreateTripSummary()]));
        screenModels.BuildAsync(Arg.Any<IReadOnlyCollection<ParcelEntity>>(), UserId, false, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ParcelScreenReadModel>());

        var result = await new GetAssistantTripParcelsQueryHandler(
                repository, tripClient, screenModels, Substitute.For<IParcelCustodyExceptionRequestRepository>())
            .Handle(new GetAssistantTripParcelsQuery(
                TripId, UserId, OperatorId, 1, 20, Role: "DRIVER"), default);

        result.Items.Should().BeEmpty();
        await tripClient.Received(1).AuthorizeCrewForTripAsync(
            TripId, UserId, OperatorId, "DRIVER", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AssignedDriver_ReturnsPendingApprovalActionsWithoutAssistantActions()
    {
        var parcel = CreateParcel();
        var incidentId = Guid.NewGuid();
        var approval = VietRide.Parcel.Domain.Entities.ParcelCustodyExceptionRequest.Create(
            parcel.Id,
            incidentId,
            OperatorId,
            TripId,
            ParcelIncidentType.WRONG_STOP,
            ParcelCustodyLocationType.ROUTE_STOP,
            Guid.NewGuid(),
            "Wrong stop",
            null,
            null,
            null,
            "[]",
            "Assistant reported a wrong stop",
            Guid.NewGuid(),
            "ASSISTANT",
            DateTimeOffset.UtcNow,
            Guid.NewGuid());
        var repository = Substitute.For<IParcelRepository>();
        var approvals = Substitute.For<IParcelCustodyExceptionRequestRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var screenModels = Substitute.For<IParcelReliabilityReadModelService>();
        tripClient.AuthorizeCrewForTripAsync(
                TripId, UserId, OperatorId, "DRIVER", Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        repository.ListByTripAndOperatorFilteredAsync(
                TripId, OperatorId, null, null, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        repository.GetAssistantManifestCountsAsync(TripId, OperatorId, null, Arg.Any<CancellationToken>())
            .Returns(new AssistantParcelManifestCounts(1, 1, 0, 0, 0, 1, 1));
        approvals.ListLatestByParcelsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([approval]);
        screenModels.BuildAsync(Arg.Any<IReadOnlyCollection<ParcelEntity>>(), UserId, false, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ParcelScreenReadModel>
            {
                [parcel.Id] = CreateScreen(
                    parcel,
                    new ReliabilityIncidentSummaryResponse(
                        incidentId,
                        "WRONG_STOP",
                        "OPEN",
                        null,
                        null,
                        "NOT_STARTED",
                        false)),
            });

        var result = await new GetAssistantTripParcelsQueryHandler(
                repository, tripClient, screenModels, approvals)
            .Handle(new GetAssistantTripParcelsQuery(
                TripId, UserId, OperatorId, 1, 20, Role: "DRIVER"), default);

        var item = result.Items.Should().ContainSingle().Subject;
        item.AvailableActions.Should().BeEquivalentTo(
            "VIEW_INCIDENT",
            "APPROVE_CUSTODY_EXCEPTION",
            "REJECT_CUSTODY_EXCEPTION");
        item.AvailableActions.Should().NotContain(["CHECK_IN", "REWEIGH", "LOAD", "CUSTODY_SCAN"]);
        item.CustodyExceptionApproval.Should().NotBeNull();
        item.CustodyExceptionApproval!.Status.Should().Be("PENDING_APPROVAL");
        item.CustodyExceptionApproval.RequestId.Should().Be(approval.Id);
    }

    [Fact]
    public async Task Handle_UnscannedHandoffOnVehicle_OffersCrewRecoveryAction()
    {
        var parcel = CreateParcel();
        typeof(ParcelEntity).GetProperty(nameof(ParcelEntity.Status))!
            .SetValue(parcel, ParcelStatus.PENDING_OPERATOR_ACTION);
        typeof(ParcelEntity).GetProperty(nameof(ParcelEntity.PendingActionType))!
            .SetValue(parcel, PendingActionType.CUSTODY_EXCEPTION);
        typeof(ParcelEntity).GetProperty(nameof(ParcelEntity.PendingActionResumeStatus))!
            .SetValue(parcel, ParcelStatus.IN_TRANSIT);
        var repository = Substitute.For<IParcelRepository>();
        var approvals = Substitute.For<IParcelCustodyExceptionRequestRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var screenModels = Substitute.For<IParcelReliabilityReadModelService>();
        tripClient.AuthorizeAssistantForTripAsync(TripId, UserId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        repository.ListByTripAndOperatorFilteredAsync(
                TripId, OperatorId, null, null, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        repository.GetAssistantManifestCountsAsync(TripId, OperatorId, null, Arg.Any<CancellationToken>())
            .Returns(new AssistantParcelManifestCounts(1, 0, 0, 0, 0, 1, 1));
        screenModels.BuildAsync(Arg.Any<IReadOnlyCollection<ParcelEntity>>(), UserId, false, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ParcelScreenReadModel>
            {
                [parcel.Id] = CreateScreen(
                    parcel,
                    new ReliabilityIncidentSummaryResponse(
                        Guid.NewGuid(),
                        "UNSCANNED_HANDOFF",
                        "SEARCHING",
                        DateTimeOffset.UtcNow.AddHours(1),
                        DateTimeOffset.UtcNow.AddMinutes(30),
                        "ON_TRACK",
                        true)),
            });

        var result = await new GetAssistantTripParcelsQueryHandler(
                repository, tripClient, screenModels, approvals)
            .Handle(new GetAssistantTripParcelsQuery(TripId, UserId, OperatorId, 1, 20), default);

        result.Items.Should().ContainSingle();
        result.Items[0].AvailableActions.Should().Contain("CONFIRM_FOUND_ON_VEHICLE");
    }

    [Fact]
    public async Task Handle_InTransitAtCurrentStop_OffersOptionalDirectCustodyScan()
    {
        var parcel = CreateParcel();
        typeof(ParcelEntity).GetProperty(nameof(ParcelEntity.Status))!
            .SetValue(parcel, ParcelStatus.IN_TRANSIT);
        var repository = Substitute.For<IParcelRepository>();
        var approvals = Substitute.For<IParcelCustodyExceptionRequestRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var screenModels = Substitute.For<IParcelReliabilityReadModelService>();
        tripClient.AuthorizeAssistantForTripAsync(TripId, UserId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        repository.ListByTripAndOperatorFilteredAsync(
                TripId, OperatorId, null, null, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        repository.GetAssistantManifestCountsAsync(TripId, OperatorId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new AssistantParcelManifestCounts(1, 0, 1, 1, 0, 0, 0));
        var screen = CreateScreen(parcel);
        screen = screen with
        {
            Trip = screen.Trip with
            {
                Stops =
                [
                    new ReliabilityTripStopResponse(
                        Guid.NewGuid(),
                        "Current stop",
                        1,
                        DateTimeOffset.UtcNow,
                        "ARRIVED",
                        DateTimeOffset.UtcNow,
                        null),
                ],
            },
        };
        screenModels.BuildAsync(Arg.Any<IReadOnlyCollection<ParcelEntity>>(), UserId, false, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ParcelScreenReadModel> { [parcel.Id] = screen });

        var result = await new GetAssistantTripParcelsQueryHandler(
                repository, tripClient, screenModels, approvals)
            .Handle(new GetAssistantTripParcelsQuery(TripId, UserId, OperatorId, 1, 20), default);

        result.Items.Should().ContainSingle();
        result.Items[0].AvailableActions.Should().Contain("CUSTODY_SCAN");
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Validator_RejectsInvalidPagination(int page, int pageSize)
    {
        var result = new GetAssistantTripParcelsQueryValidator()
            .Validate(new GetAssistantTripParcelsQuery(TripId, UserId, OperatorId, page, pageSize));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void OperationalLocationResponse_UsesSharedContractTimestampNames()
    {
        var timestamp = DateTimeOffset.Parse("2026-08-28T01:00:00+07:00");
        var response = new AssistantOperationalLocationResponse(
            new ReliabilityLocationResponse("ROUTE_STOP", Guid.NewGuid(), "Stop 1"),
            "ARRIVED",
            timestamp,
            null);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        json.RootElement.GetProperty("actualArrivalAt").GetDateTimeOffset().Should().Be(timestamp);
        json.RootElement.GetProperty("actualDepartureAt").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.TryGetProperty("arrivedAt", out _).Should().BeFalse();
        json.RootElement.TryGetProperty("departedAt", out _).Should().BeFalse();
    }

    private static ParcelEntity CreateParcel()
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VR-PCL-001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Nguyen Van A",
            PhoneNumber.Normalize("0900000000"),
            null,
            OperatorId,
            TripId,
            Guid.NewGuid(),
            null,
            "Goi hang nho",
            PhotoUrl,
            ParcelSizeCategory.SMALL,
            2.5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));
        typeof(ParcelEntity)
            .GetProperty(nameof(ParcelEntity.Status), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(parcel, ParcelStatus.CHECKED_IN);
        return parcel;
    }

    private static ParcelScreenReadModel CreateScreen(
        ParcelEntity parcel,
        ReliabilityIncidentSummaryResponse? incident = null)
        => new(
            new ReliabilityParcelSummaryResponse(
                parcel.Id,
                parcel.ParcelCode,
                parcel.Status.ToString(),
                parcel.Description,
                parcel.PhotoUrl,
                parcel.Quantity,
                parcel.DeclaredValueVnd),
            new ReliabilityOperatorResponse(OperatorId, "Operator", null, null),
            new ReliabilityTripResponse(
                TripId,
                "BOARDING",
                null,
                null,
                null,
                null,
                []),
            null,
            new ReliabilityLocationResponse("ROUTE_STOP", parcel.DropoffStopId, null),
            new ParcelReliabilitySummaryResponse(null, incident, null, null, []));

    private static TripSummarySnapshot CreateTripSummary()
        => new(
            TripId,
            "BOARDING",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            new TripRouteSummarySnapshot(Guid.NewGuid(), "Route", "Origin", "Destination"),
            new TripVehicleSummarySnapshot(Guid.NewGuid(), "51B-12345", "ACTIVE"));
}
