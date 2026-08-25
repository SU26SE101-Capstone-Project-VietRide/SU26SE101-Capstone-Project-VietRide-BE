using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.TripEvents;
using VietRide.Parcel.Infrastructure.Messaging;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.UnitTests.Features.Parcels;

public sealed class Day35VehicleSubstitutedConsumerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExactEvent_ValidatesAndDispatchesCanonicalIds()
    {
        var mediator = Substitute.For<IMediator>();
        var handler = CreateHandler(mediator);
        var integrationEvent = Event();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<HandleVehicleSubstitutedCommand>(command =>
                command.EventId == integrationEvent.EventId
                && command.OldTripId == integrationEvent.OldTripId
                && command.NewTripId == integrationEvent.NewTripId
                && command.OperatorId == integrationEvent.OperatorId
                && command.Reason == integrationEvent.Reason),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CanonicalUtcOffsetJson_DeserializesAndDispatches()
    {
        var serializerOptions = UtcJson.Options;
        var json = JsonSerializer.Serialize(Event(), serializerOptions);
        json.Should().Contain("\"occurredAt\":\"2026-07-30T05:00:00Z\"");
        json.Should().NotContain("+00:00");
        var integrationEvent = JsonSerializer.Deserialize<TripVehicleSubstitutedIntegrationEvent>(
            json,
            serializerOptions)!;
        var mediator = Substitute.For<IMediator>();

        await CreateHandler(mediator).HandleAsync(integrationEvent, CancellationToken.None);

        integrationEvent.OccurredAt.Should().Be(Now);
        await mediator.Received(1).Send(
            Arg.Is<HandleVehicleSubstitutedCommand>(command =>
                command.EventId == integrationEvent.EventId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MismatchedCanonicalTimestamps_AreRejectedBeforeDispatch()
    {
        var mediator = Substitute.For<IMediator>();
        var invalid = Event() with { DisruptedAt = Now.AddSeconds(1) };

        var act = () => CreateHandler(mediator).HandleAsync(invalid, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*invalid timestamp*");
        await mediator.DidNotReceiveWithAnyArgs().Send(
            default(IRequest<int>)!,
            default);
    }

    [Fact]
    public async Task LegacyOrSemanticallyInvalidEvent_IsRejectedBeforeDispatch()
    {
        var json = JsonSerializer.Serialize(Event());
        var withLegacyField = json[..^1] + ",\"legacyTripId\":\"x\"}";

        var deserialize = () => JsonSerializer.Deserialize<
            TripVehicleSubstitutedIntegrationEvent>(withLegacyField);
        deserialize.Should().Throw<JsonException>();

        var mediator = Substitute.For<IMediator>();
        var invalid = Event() with { NewTripStatus = "SCHEDULED" };
        var act = () => CreateHandler(mediator)
            .HandleAsync(invalid, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
        await mediator.DidNotReceiveWithAnyArgs().Send(
            default(IRequest<int>)!,
            default);
    }

    [Fact]
    public async Task ApplicationTransition_PassesOperatorGuardAndCanonicalEventIdentity()
    {
        var integrationEvent = Event();
        var repository = Substitute.For<IParcelRepository>();
        repository.TryBulkRequestTransferByTripIdAsync(
                integrationEvent.OldTripId,
                integrationEvent.NewTripId,
                integrationEvent.OperatorId,
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ParcelEventSnapshot>());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var affected = await new HandleVehicleSubstitutedCommandHandler(
                repository,
                Substitute.For<IIntegrationEventOutbox>(),
                clock)
            .Handle(
                new HandleVehicleSubstitutedCommand(
                    integrationEvent.EventId,
                    integrationEvent.OldTripId,
                    integrationEvent.NewTripId,
                    integrationEvent.OperatorId,
                    integrationEvent.Reason),
                CancellationToken.None);

        affected.Should().Be(0);
        await repository.Received(1).TryBulkRequestTransferByTripIdAsync(
            integrationEvent.OldTripId,
            integrationEvent.NewTripId,
            integrationEvent.OperatorId,
            Now,
            Arg.Any<CancellationToken>());
    }

    private static IIntegrationEventHandler<TripVehicleSubstitutedIntegrationEvent>
        CreateHandler(IMediator mediator)
    {
        var type = typeof(TripVehicleSubstitutedIntegrationEvent).Assembly.GetType(
            "VietRide.Parcel.Infrastructure.Messaging.TripVehicleSubstitutedIntegrationEventHandler",
            throwOnError: true)!;
        return (IIntegrationEventHandler<TripVehicleSubstitutedIntegrationEvent>)
            Activator.CreateInstance(
                type,
                BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic,
                binder: null,
                args: [mediator],
                culture: null)!;
    }

    private static TripVehicleSubstitutedIntegrationEvent Event()
    {
        var eventId = Guid.NewGuid();
        return new TripVehicleSubstitutedIntegrationEvent
        {
            EventId = eventId,
            OccurredAt = Now,
            SubstitutionId = eventId,
            DisruptedAt = Now,
            OperatorId = Guid.NewGuid(),
            OldTripId = Guid.NewGuid(),
            OldTripStatus = "DISRUPTED",
            OldVehicleId = Guid.NewGuid(),
            NewTripId = Guid.NewGuid(),
            NewTripStatus = "BOARDING",
            NewVehicleId = Guid.NewGuid(),
            NewVehiclePlateNumber = "51B-12345",
            NewTripDepartureDateTime = Now.AddMinutes(15),
            ActorUserId = Guid.NewGuid(),
            Reason = "Vehicle breakdown",
            NotifyPassengers = true,
            Mappings =
            [
                new TripVehicleSubstitutionMapping
                {
                    BookingId = Guid.NewGuid(),
                    PassengerId = Guid.NewGuid(),
                    OriginalSeatNumber = "A1",
                    NewSeatNumber = "B1",
                    OriginalSeatType = "STANDARD",
                    NewSeatType = "VIP",
                    IsSeatDowngrade = false,
                    OriginalBoardingStatus = "BOARDED",
                },
            ],
        };
    }
}
