using System.Reflection;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Reliability.ReportIncident;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class ReportParcelIncidentTests
{
    [Fact]
    public async Task PassengerReport_AfterDeliveryConfirmed_IsRejectedWithoutWrites()
    {
        var fixture = CreateFixture(ParcelStatus.DELIVERY_CONFIRMED);

        var action = () => fixture.Handler.Handle(
            new ReportParcelIncidentCommand(
                fixture.Parcel.Id,
                fixture.Parcel.SenderUserId,
                null,
                "DAMAGED",
                "Package damaged.",
                []),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("PARCEL_INCIDENT_STATUS_NOT_REPORTABLE");
        await fixture.Custody.DidNotReceiveWithAnyArgs().AppendAsync(
            default!, default, default, default, default, default, default!, default!, default,
            default, default, default);
        await fixture.Reliability.DidNotReceiveWithAnyArgs().AddIncidentAsync(default!, default);
        await fixture.Outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default!, default);
    }

    [Fact]
    public async Task PassengerReport_WhileUnloaded_CommitsTheAllowedIncidentShape()
    {
        var fixture = CreateFixture(ParcelStatus.UNLOADED);
        fixture.Parcels.TrySetPendingOperatorActionAsync(
                fixture.Parcel.Id,
                PendingActionType.CUSTODY_EXCEPTION,
                Arg.Any<string>(),
                null,
                fixture.Now,
                Arg.Any<CancellationToken>(),
                ParcelStatus.UNLOADED)
            .Returns(true);
        var custodyEvent = ParcelCustodyEvent.Create(
            fixture.Parcel.Id,
            null,
            fixture.Parcel.TripId,
            ParcelCustodyEventType.EXCEPTION_REPORTED,
            null,
            null,
            null,
            null,
            null,
            null,
            fixture.Parcel.SenderUserId,
            "USER",
            fixture.Now,
            "INCIDENT_REPORT",
            "incident-test",
            null,
            null,
            1);
        fixture.Custody.AppendAsync(
                fixture.Parcel,
                Arg.Any<ParcelCustodyEventType>(),
                Arg.Any<ParcelCustodyLocationType?>(),
                Arg.Any<Guid?>(),
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(custodyEvent);

        var response = await fixture.Handler.Handle(
            new ReportParcelIncidentCommand(
                fixture.Parcel.Id,
                fixture.Parcel.SenderUserId,
                null,
                "DELIVERY_NOT_RECEIVED",
                "Recipient did not receive the package.",
                []),
            CancellationToken.None);

        response.IncidentType.Should().Be("DELIVERY_NOT_RECEIVED");
        await fixture.Reliability.Received(1).AddIncidentAsync(
            Arg.Is<ParcelIncident>(incident => incident.Type == ParcelIncidentType.DELIVERY_NOT_RECEIVED),
            Arg.Any<CancellationToken>());
        await fixture.Reliability.Received(3).AddSearchTaskAsync(
            Arg.Any<ParcelSearchTask>(),
            Arg.Any<CancellationToken>());
    }

    private static Fixture CreateFixture(ParcelStatus status)
    {
        var now = new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero);
        var parcel = ParcelEntity.CreatePendingPayment(
            "VR-INCIDENT-POLICY-001",
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
            2m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));
        typeof(ParcelEntity).GetProperty(
                nameof(parcel.Status),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(parcel, status);
        var parcels = Substitute.For<IParcelRepository>();
        parcels.AcquireForIncidentReportAsync(parcel.Id, Arg.Any<CancellationToken>())
            .Returns(parcel);
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetOpenIncidentAsync(
                parcel.Id,
                Arg.Any<ParcelIncidentType>(),
                Arg.Any<CancellationToken>())
            .Returns((ParcelIncident?)null);
        reliability.GetCurrentCustodyAsync(parcel.Id, Arg.Any<CancellationToken>())
            .Returns((ParcelCurrentCustody?)null);
        var custody = Substitute.For<IParcelCustodyService>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        return new Fixture(
            new ReportParcelIncidentCommandHandler(parcels, reliability, custody, outbox, clock),
            parcel,
            parcels,
            reliability,
            custody,
            outbox,
            now);
    }

    private sealed record Fixture(
        ReportParcelIncidentCommandHandler Handler,
        ParcelEntity Parcel,
        IParcelRepository Parcels,
        IParcelReliabilityRepository Reliability,
        IParcelCustodyService Custody,
        IIntegrationEventOutbox Outbox,
        DateTimeOffset Now);
}
