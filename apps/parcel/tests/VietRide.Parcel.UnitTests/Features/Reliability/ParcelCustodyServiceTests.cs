using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class ParcelCustodyServiceTests
{
    [Fact]
    public async Task AppendLoaded_CreatesActiveTransitLeg()
    {
        var now = new DateTimeOffset(2026, 8, 21, 8, 0, 0, TimeSpan.Zero);
        var parcel = CreateParcel();
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetActiveLegAsync(parcel.Id, Arg.Any<CancellationToken>())
            .Returns((ParcelTransitLeg?)null);
        reliability.GetLatestTransitLegAsync(parcel.Id, Arg.Any<CancellationToken>())
            .Returns((ParcelTransitLeg?)null);
        reliability.ListCustodyEventsAsync(parcel.Id, Arg.Any<CancellationToken>())
            .Returns([]);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        ParcelTransitLeg? addedLeg = null;
        reliability.AddTransitLegAsync(
                Arg.Do<ParcelTransitLeg>(leg => addedLeg = leg),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await CreateService(reliability, clock).AppendAsync(
            parcel,
            ParcelCustodyEventType.LOADED,
            ParcelCustodyLocationType.ORIGIN_STATION,
            null,
            "Origin station",
            Guid.NewGuid(),
            "ASSISTANT",
            "LOAD",
            Guid.NewGuid().ToString("D"),
            null,
            null);

        addedLeg.Should().NotBeNull();
        addedLeg!.Status.Should().Be(ParcelTransitLegStatus.ACTIVE);
        addedLeg.StartedAt.Should().Be(now);
        await reliability.DidNotReceive().UpdateTransitLegAsync(
            Arg.Any<ParcelTransitLeg>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendUnloaded_CompletesActiveTransitLegAtActualDestination()
    {
        var now = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var parcel = CreateParcel();
        var leg = CreateLeg(parcel);
        leg.Start(now.AddHours(-1));
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetActiveLegAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(leg);
        reliability.ListCustodyEventsAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns([]);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        await CreateService(reliability, clock).AppendAsync(
            parcel,
            ParcelCustodyEventType.UNLOADED,
            ParcelCustodyLocationType.ROUTE_STOP,
            parcel.DropoffStopId,
            "Expected stop",
            Guid.NewGuid(),
            "ASSISTANT",
            "UNLOAD",
            Guid.NewGuid().ToString("D"),
            null,
            null);

        leg.Status.Should().Be(ParcelTransitLegStatus.COMPLETED);
        leg.ActualDestinationId.Should().Be(parcel.DropoffStopId);
        leg.EndedAt.Should().Be(now);
        await reliability.Received(1).UpdateTransitLegAsync(leg, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendDelivered_ReusesCompletedLegWithoutCreatingPhantomLeg()
    {
        var now = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
        var parcel = CreateParcel();
        var leg = CreateLeg(parcel);
        leg.Start(now.AddHours(-2));
        leg.Complete(parcel.DropoffStopId, now.AddMinutes(-10));
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetActiveLegAsync(parcel.Id, Arg.Any<CancellationToken>())
            .Returns((ParcelTransitLeg?)null);
        reliability.GetLatestTransitLegAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(leg);
        reliability.ListCustodyEventsAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns([]);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        ParcelCustodyEvent? addedEvent = null;
        reliability.AddCustodyEventAsync(
                Arg.Do<ParcelCustodyEvent>(custodyEvent => addedEvent = custodyEvent),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await CreateService(reliability, clock).AppendAsync(
            parcel,
            ParcelCustodyEventType.DELIVERED,
            ParcelCustodyLocationType.ROUTE_STOP,
            parcel.DropoffStopId,
            "Expected stop",
            null,
            "RECIPIENT",
            "DELIVERY_CONFIRMATION",
            Guid.NewGuid().ToString("D"),
            null,
            null);

        addedEvent.Should().NotBeNull();
        addedEvent!.LegId.Should().Be(leg.Id);
        await reliability.DidNotReceive().AddTransitLegAsync(
            Arg.Any<ParcelTransitLeg>(),
            Arg.Any<CancellationToken>());
        await reliability.DidNotReceive().UpdateTransitLegAsync(
            Arg.Any<ParcelTransitLeg>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendFoundOnVehicle_UsesConfirmedOperationalVehicleId()
    {
        var parcel = CreateParcel();
        var confirmedVehicleId = Guid.NewGuid();
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetActiveLegAsync(parcel.Id, Arg.Any<CancellationToken>())
            .Returns((ParcelTransitLeg?)null);
        reliability.GetLatestTransitLegAsync(parcel.Id, Arg.Any<CancellationToken>())
            .Returns((ParcelTransitLeg?)null);
        reliability.ListCustodyEventsAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns([]);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        ParcelCustodyEvent? addedEvent = null;
        reliability.AddCustodyEventAsync(
                Arg.Do<ParcelCustodyEvent>(custodyEvent => addedEvent = custodyEvent),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await CreateService(reliability, clock).AppendAsync(
            parcel,
            ParcelCustodyEventType.FOUND,
            ParcelCustodyLocationType.VEHICLE,
            confirmedVehicleId,
            $"VEHICLE:{confirmedVehicleId:D}",
            Guid.NewGuid(),
            "ASSISTANT",
            "CREW_FOUND_ON_VEHICLE",
            Guid.NewGuid().ToString("D"),
            null,
            null);

        addedEvent.Should().NotBeNull();
        addedEvent!.ActualLocationId.Should().Be(confirmedVehicleId);
        addedEvent.VehicleId.Should().Be(confirmedVehicleId);
    }

    private static ParcelCustodyService CreateService(
        IParcelReliabilityRepository reliability,
        IClock clock)
        => new(reliability, Substitute.For<IIntegrationEventOutbox>(), clock);

    private static ParcelEntity CreateParcel()
        => ParcelEntity.CreatePendingPayment(
            "VR-CUSTODY-001",
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

    private static ParcelTransitLeg CreateLeg(ParcelEntity parcel)
        => ParcelTransitLeg.Create(
            parcel.Id,
            parcel.TripId,
            parcel.OperatorId,
            1,
            null,
            parcel.DropoffStopId,
            "Origin station",
            "Expected stop",
            Guid.NewGuid(),
            "51B-12345");
}
