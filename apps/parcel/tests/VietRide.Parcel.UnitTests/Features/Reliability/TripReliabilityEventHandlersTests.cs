using System.Reflection;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class TripReliabilityEventHandlersTests
{
    [Fact]
    public async Task StopDeparted_WhenStatusTransitionLosesRace_DoesNotOpenIncident()
    {
        var tripId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var parcel = CreateParcel(tripId, stopId, ParcelStatus.IN_TRANSIT);
        var parcels = Substitute.For<IParcelRepository>();
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        parcels.ListPendingDropoffByTripAndStopAsync(tripId, stopId, Arg.Any<CancellationToken>())
            .Returns(new[] { parcel });
        parcels.TrySetPendingOperatorActionAsync(
                parcel.Id,
                PendingActionType.CUSTODY_EXCEPTION,
                Arg.Any<string>(),
                null,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>(),
                ParcelStatus.IN_TRANSIT)
            .Returns(false);

        var result = await new HandleTripStopDepartedWithPendingCommandHandler(parcels, reliability, outbox)
            .Handle(new HandleTripStopDepartedWithPendingCommand(tripId, stopId, DateTimeOffset.UtcNow), CancellationToken.None);

        result.Should().Be(0);
        await reliability.DidNotReceive().AddIncidentAsync(
            Arg.Any<ParcelIncident>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DestinationArrived_WithTerminalParcelStillInTransit_OpensSearchIncident()
    {
        var tripId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var arrivedAt = DateTimeOffset.UtcNow;
        var parcel = CreateParcel(tripId, null, ParcelStatus.IN_TRANSIT);
        var parcels = Substitute.For<IParcelRepository>();
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        parcels.ListPendingTerminalDropoffByTripAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(new[] { parcel });
        parcels.TrySetPendingOperatorActionAsync(
                parcel.Id,
                PendingActionType.CUSTODY_EXCEPTION,
                Arg.Any<string>(),
                null,
                arrivedAt,
                Arg.Any<CancellationToken>(),
                ParcelStatus.IN_TRANSIT)
            .Returns(true);
        ParcelIncident? addedIncident = null;
        reliability.AddIncidentAsync(Arg.Do<ParcelIncident>(value => addedIncident = value), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await new HandleTripDestinationArrivedCommandHandler(parcels, reliability, outbox)
            .Handle(new HandleTripDestinationArrivedCommand(tripId, stationId, arrivedAt), CancellationToken.None);

        result.Should().Be(1);
        addedIncident.Should().NotBeNull();
        addedIncident!.Type.Should().Be(ParcelIncidentType.MISSING);
        addedIncident.Status.Should().Be(ParcelIncidentStatus.SEARCHING);
        addedIncident.ExpectedLocation.Should().Be($"DESTINATION_STATION:{stationId:D}");
        await reliability.Received(2).AddSearchTaskAsync(
            Arg.Any<ParcelSearchTask>(),
            Arg.Any<CancellationToken>());
        await parcels.Received(1).TrySetPendingOperatorActionAsync(
            parcel.Id,
            PendingActionType.CUSTODY_EXCEPTION,
            Arg.Any<string>(),
            null,
            arrivedAt,
            Arg.Any<CancellationToken>(),
            ParcelStatus.IN_TRANSIT);
    }

    private static ParcelEntity CreateParcel(Guid tripId, Guid? dropoffStopId, ParcelStatus status)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-TERMINAL",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            "recipient@example.com",
            Guid.NewGuid(),
            tripId,
            dropoffStopId,
            null,
            "Item",
            null,
            ParcelSizeCategory.MEDIUM,
            5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));
        Set(parcel, nameof(parcel.Status), status);
        return parcel;
    }

    private static void Set<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property!.SetValue(target, value);
    }
}
