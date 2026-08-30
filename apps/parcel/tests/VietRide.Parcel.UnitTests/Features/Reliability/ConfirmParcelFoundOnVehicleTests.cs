using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using VietRide.Parcel.Api.Controllers;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class ConfirmParcelFoundOnVehicleTests
{
    [Fact]
    public void ControllerSurface_UsesCanonicalAssistantRouteAndRequiresIdempotency()
    {
        var method = typeof(AssistantParcelsController)
            .GetMethod(nameof(AssistantParcelsController.ConfirmFoundOnVehicleAsync));

        method.Should().NotBeNull();
        method!.GetCustomAttribute<HttpPostAttribute>()!.Template
            .Should().Be("{parcelId:guid}/confirm-found-on-vehicle");
        method!.GetCustomAttribute<RequireIdempotencyKeyAttribute>().Should().NotBeNull();
    }

    [Theory]
    [InlineData(ParcelIncidentType.MISSING, "SYSTEM")]
    [InlineData(ParcelIncidentType.UNSCANNED_HANDOFF, "ASSISTANT")]
    public async Task AssignedAssistant_ConfirmsRecoverableParcelOnVehicle_AndRestoresTransportState(
        ParcelIncidentType incidentType,
        string reporterSource)
    {
        var parcel = CreatePendingParcel();
        var incident = ParcelIncident.Open(
            parcel.Id,
            parcel.OperatorId,
            incidentType,
            DateTimeOffset.UtcNow.AddHours(72),
            parcel.TripId,
            null,
            null,
            reporterSource,
            "DESTINATION_STATION",
            "VEHICLE",
            "Trip completed with parcel still pending.",
            null,
            operatorProcessBreach: true);
        incident.StartSearch();

        var assistantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var custodyEvent = ParcelCustodyEvent.Create(
            parcel.Id,
            null,
            parcel.TripId,
            ParcelCustodyEventType.FOUND,
            ParcelCustodyLocationType.DESTINATION_STATION,
            null,
            ParcelCustodyLocationType.VEHICLE,
            vehicleId,
            $"VEHICLE:{vehicleId:D}",
            vehicleId,
            assistantId,
            "ASSISTANT",
            now,
            "CREW_FOUND_ON_VEHICLE",
            "assistant-found-on-vehicle:test",
            null,
            "Found in cargo bay.",
            2);

        var parcels = Substitute.For<IParcelRepository>();
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        var trips = Substitute.For<ITripServiceClient>();
        var custody = Substitute.For<IParcelCustodyService>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        parcels.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);
        reliability.GetIncidentAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);
        reliability.ListSearchTasksAsync(incident.Id, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelSearchTask>());
        trips.AuthorizeAssistantForTripAsync(
                parcel.TripId,
                assistantId,
                parcel.OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        trips.GetTripOperationalLocationAsync(parcel.TripId, Arg.Any<CancellationToken>())
            .Returns(new TripOperationalLocationOutcome(
                TripOperationalLocationOutcomeKind.Success,
                new TripOperationalLocationSnapshot(
                    parcel.TripId,
                    vehicleId,
                    "COMPLETED",
                    null,
                    null,
                    null,
                    null,
                    now),
                null));
        custody.AppendAsync(
                parcel,
                ParcelCustodyEventType.FOUND,
                ParcelCustodyLocationType.VEHICLE,
                vehicleId,
                Arg.Any<string>(),
                assistantId,
                "ASSISTANT",
                "CREW_FOUND_ON_VEHICLE",
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(custodyEvent);
        parcels.TryResolvePendingOperatorActionAsync(
                parcel.Id,
                PendingActionType.CUSTODY_EXCEPTION,
                now,
                Arg.Any<CancellationToken>())
            .Returns(new ParcelPaymentTransitionSnapshot(
                parcel.Id,
                parcel.ParcelCode,
                ParcelStatus.IN_TRANSIT,
                0,
                0,
                parcel.OperatorId,
                parcel.TripId,
                null,
                parcel.SenderUserId,
                ParcelSizeCategory.MEDIUM,
                null));

        var result = await new ConfirmParcelFoundOnVehicleCommandHandler(
                parcels,
                reliability,
                trips,
                custody,
                outbox,
                clock)
            .Handle(
                new ConfirmParcelFoundOnVehicleCommand(
                    parcel.Id,
                    incident.Id,
                    parcel.OperatorId,
                    assistantId,
                    parcel.ParcelCode,
                    null,
                    "Found in cargo bay.",
                    Guid.NewGuid()),
                CancellationToken.None);

        result.IncidentId.Should().Be(incident.Id);
        result.CustodyEventId.Should().Be(custodyEvent.Id);
        incident.Status.Should().Be(ParcelIncidentStatus.RESOLVED);
        incident.ResolutionCode.Should().Be("CREW_CONFIRMED_ON_VEHICLE");
        await parcels.Received(1).TryResolvePendingOperatorActionAsync(
            parcel.Id,
            PendingActionType.CUSTODY_EXCEPTION,
            now,
            Arg.Any<CancellationToken>());
    }

    private static ParcelEntity CreatePendingParcel()
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VR-PCL-FOUND-001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            "recipient@example.com",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            "Documents",
            null,
            ParcelSizeCategory.MEDIUM,
            5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));
        Set(parcel, nameof(parcel.Status), ParcelStatus.PENDING_OPERATOR_ACTION);
        Set(parcel, nameof(parcel.PendingActionType), (PendingActionType?)PendingActionType.CUSTODY_EXCEPTION);
        Set(parcel, nameof(parcel.PendingActionResumeStatus), (ParcelStatus?)ParcelStatus.IN_TRANSIT);
        return parcel;
    }

    private static void Set<T>(object target, string propertyName, T value)
    {
        target.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(target, value);
    }
}
