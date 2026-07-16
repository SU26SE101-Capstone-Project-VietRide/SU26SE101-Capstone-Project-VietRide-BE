using System.Text.Json;
using FluentAssertions;
using MediatR;
using NSubstitute;
using VietRide.Booking.Application.Features.Bookings.HandleVehicleSwap;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.IntegrationTests.Messaging;

public sealed class TripVehicleSwappedIntegrationEventHandlerTests
{
    [Fact]
    public async Task HandlerMapsExactPayloadAndReturnsOnlyAfterCommandSucceeds()
    {
        var mediator = Substitute.For<IMediator>();
        HandleVehicleSwapCommand? captured = null;
        mediator.Send(Arg.Do<HandleVehicleSwapCommand>(command => captured = command), Arg.Any<CancellationToken>())
            .Returns(1);
        var handler = CreateHandler(mediator);
        var occurredAt = new DateTime(2026, 7, 15, 2, 0, 0, DateTimeKind.Utc);
        var source = new TripVehicleSwappedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            OccurredAt = occurredAt,
            TripId = Guid.NewGuid(),
            OperatorId = Guid.NewGuid(),
            OldVehicleId = Guid.NewGuid(),
            NewVehicleId = Guid.NewGuid(),
            OldVehiclePlateNumber = "51A-000.01",
            NewVehiclePlateNumber = "51A-000.02",
            DepartureDateTime = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero),
            DriverUserId = Guid.NewGuid(),
            AssistantUserId = null,
            SeatImpacts =
            [
                new TripVehicleSwapSeatImpact
                {
                    BookingId = Guid.NewGuid(),
                    SeatNumbers = ["A01", "A02"],
                    Reason = "SEAT_DISABLED",
                },
            ],
        };

        await handler.HandleAsync(source, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.EventId.Should().Be(source.EventId);
        captured.OccurredAt.Should().Be(new DateTimeOffset(occurredAt));
        captured.TripId.Should().Be(source.TripId);
        captured.OperatorId.Should().Be(source.OperatorId);
        captured.DepartureDateTime.Should().Be(source.DepartureDateTime);
        captured.SeatImpacts.Should().ContainSingle().Which.Should().BeEquivalentTo(source.SeatImpacts.Single());
    }

    [Fact]
    public async Task SchedulerFailureFromCommandPropagatesToSharedConsumerDlqPolicy()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<HandleVehicleSwapCommand>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("scheduler failed"));
        var handler = CreateHandler(mediator);

        var act = () => handler.HandleAsync(CreateValidEvent(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void ContractRejectsExtraMembersAndRequiresPresentNullableAssistant()
    {
        var validJson = JsonSerializer.Serialize(CreateValidEvent(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var extraJson = validJson[..^1] + ",\"unexpected\":true}";
        using var document = JsonDocument.Parse(validJson);
        var withoutAssistant = JsonSerializer.Serialize(
            document.RootElement.EnumerateObject()
                .Where(property => property.Name != "assistantUserId")
                .ToDictionary(property => property.Name, property => property.Value),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        JsonSerializer.Deserialize<TripVehicleSwappedIntegrationEvent>(validJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).Should().NotBeNull();
        var extraAct = () => JsonSerializer.Deserialize<TripVehicleSwappedIntegrationEvent>(extraJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var missingAct = () => JsonSerializer.Deserialize<TripVehicleSwappedIntegrationEvent>(withoutAssistant,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        extraAct.Should().Throw<JsonException>();
        missingAct.Should().Throw<JsonException>();
    }

    [Fact]
    public async Task InvalidIdsTimestampsPlatesAndSeatDetailsAreRejectedBeforeMediator()
    {
        var mediator = Substitute.For<IMediator>();
        var handler = CreateHandler(mediator);
        var invalidEvents = new[]
        {
            CreateValidEvent() with { OperatorId = Guid.Empty },
            CreateValidEvent() with { OccurredAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Local) },
            CreateValidEvent() with { NewVehiclePlateNumber = " " },
            CreateValidEvent() with
            {
                SeatImpacts = [CreateValidEvent().SeatImpacts.Single() with { SeatNumbers = [" "] }],
            },
            CreateValidEvent() with
            {
                SeatImpacts = [CreateValidEvent().SeatImpacts.Single() with { Reason = "RENAMED_REASON" }],
            },
        };

        foreach (var invalidEvent in invalidEvents)
        {
            var act = () => handler.HandleAsync(invalidEvent, CancellationToken.None);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        await mediator.DidNotReceiveWithAnyArgs().Send(default!, default);
    }

    private static TripVehicleSwappedIntegrationEvent CreateValidEvent()
        => new()
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 7, 15, 2, 0, 0, DateTimeKind.Utc),
            TripId = Guid.NewGuid(),
            OperatorId = Guid.NewGuid(),
            OldVehicleId = Guid.NewGuid(),
            NewVehicleId = Guid.NewGuid(),
            OldVehiclePlateNumber = "51A-000.01",
            NewVehiclePlateNumber = "51A-000.02",
            DepartureDateTime = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero),
            DriverUserId = Guid.NewGuid(),
            AssistantUserId = null,
            SeatImpacts =
            [
                new TripVehicleSwapSeatImpact
                {
                    BookingId = Guid.NewGuid(),
                    SeatNumbers = ["A01"],
                    Reason = "SEAT_REMOVED",
                },
            ],
        };

    private static IIntegrationEventHandler<TripVehicleSwappedIntegrationEvent> CreateHandler(IMediator mediator)
    {
        var handlerType = typeof(TripVehicleSwappedIntegrationEvent).Assembly.GetType(
            "VietRide.Booking.Infrastructure.Messaging.TripVehicleSwappedIntegrationEventHandler",
            throwOnError: true)!;
        return (IIntegrationEventHandler<TripVehicleSwappedIntegrationEvent>)Activator.CreateInstance(
            handlerType,
            mediator)!;
    }
}
