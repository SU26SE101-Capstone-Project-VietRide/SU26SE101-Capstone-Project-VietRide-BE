using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.VehicleSubstitution;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.Infrastructure.DependencyInjection;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Messaging.RabbitMq;
using VietRide.Shared.Persistence.Inbox;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.Messaging;

public sealed class TripVehicleSubstitutedConsumerTests
{
    private static readonly DateTimeOffset OccurredAt =
        DateTimeOffset.Parse("2026-07-26T03:00:00Z");

    [Fact]
    public async Task AppliesEligibleBookingAndPassengerRulesWithoutChangingBoardingOrTickets()
    {
        await WithDatabaseAsync(async (dataSource, oldTripId, operatorId) =>
        {
            var booking = CreateConfirmedBooking(oldTripId, operatorId, "A01", "A02");
            booking.Passengers[0].MarkBoarded(OccurredAt.AddHours(-1), Guid.NewGuid());
            var originalBoardedAt = booking.Passengers[0].BoardedAt;
            var originalStopId = booking.Passengers[0].BoardedAtStopId;
            var originalTickets = booking.Tickets
                .Select(ticket => new { ticket.Id, ticket.SeatNumber, ticket.Status, ticket.UsedAt })
                .ToArray();
            await SeedAsync(dataSource, booking);

            var evt = CreateEvent(
                oldTripId,
                operatorId,
                booking,
                (booking.Passengers[0], "B01", "BOARDED"),
                (booking.Passengers[1], "B02", "PENDING"));
            evt = evt with
            {
                Mappings = evt.Mappings.Select((mapping, index) => mapping with
                {
                    OriginalSeatType = index == 0 ? "VIP" : "STANDARD",
                    NewSeatType = "STANDARD",
                    IsSeatDowngrade = index == 0,
                }).ToArray(),
            };
            await ConsumeAsync(dataSource, evt);

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, OccurredAt);
            var persisted = await verify.Bookings
                .AsNoTracking()
                .Include(row => row.Passengers)
                .Include(row => row.Tickets)
                .SingleAsync();
            persisted.TripId.Should().Be(evt.NewTripId);
            persisted.Status.Should().Be(BookingStatus.CONFIRMED);
            persisted.Passengers.Single(row => row.Id == booking.Passengers[0].Id).Should().Match<Passenger>(
                row => row.SeatNumber == "B01"
                    && row.BoardingStatus == PassengerBoardingStatus.BOARDED
                    && row.BoardedAt == originalBoardedAt
                    && row.BoardedAtStopId == originalStopId);
            persisted.Passengers.Single(row => row.Id == booking.Passengers[1].Id).Should().Match<Passenger>(
                row => row.SeatNumber == "B02"
                    && row.BoardingStatus == PassengerBoardingStatus.PENDING
                    && row.BoardedAt == null
                    && row.BoardedAtStopId == null);
            persisted.Tickets.Select(ticket => new
            {
                ticket.Id,
                ticket.SeatNumber,
                ticket.Status,
                ticket.UsedAt,
            }).Should().BeEquivalentTo(originalTickets);

            var transfers = await verify.BookingTransfers.AsNoTracking()
                .OrderBy(row => row.PassengerId)
                .ToArrayAsync();
            transfers.Should().HaveCount(2);
            transfers.Single(row => row.PassengerId == booking.Passengers[0].Id)
                .ConfirmationStatus.Should().Be(BookingTransferConfirmationStatus.PENDING_CONFIRM);
            transfers.Single(row => row.PassengerId == booking.Passengers[1].Id)
                .ConfirmationStatus.Should().Be(BookingTransferConfirmationStatus.NOT_REQUIRED);
            transfers.Single(row => row.PassengerId == booking.Passengers[0].Id)
                .Should().Match<BookingTransfer>(row =>
                    row.OriginalSeatType == "VIP"
                    && row.NewSeatType == "STANDARD"
                    && row.IsSeatDowngrade);
            transfers.Should().OnlyContain(row =>
                row.OriginalTripId == oldTripId
                && row.NewTripId == evt.NewTripId
                && row.ConfirmedAt == null
                && row.ConfirmedByUserId == null);
        });
    }

    [Fact]
    public async Task ExcludesNoShowAndIneligibleBookingsAndPreservesNullableSeatSemantics()
    {
        await WithDatabaseAsync(async (dataSource, oldTripId, operatorId) =>
        {
            var eligible = CreateConfirmedBooking(oldTripId, operatorId, "A01", "A02");
            eligible.Passengers[0].MarkBoarded(OccurredAt.AddHours(-1));
            eligible.MarkPendingPassengersNoShow();
            eligible.Status.Should().Be(BookingStatus.PARTIAL_NO_SHOW);
            var wrongOperator = CreateConfirmedBooking(oldTripId, Guid.NewGuid(), "C01");
            await SeedAsync(dataSource, eligible, wrongOperator);

            var evt = CreateEvent(
                oldTripId,
                operatorId,
                eligible,
                [
                    (eligible.Passengers[0], null, "BOARDED"),
                    (eligible.Passengers[1], "B02", "PENDING"),
                ],
                additionalMappings:
                [
                    new TripVehicleSubstitutedMapping
                    {
                        BookingId = wrongOperator.Id,
                        PassengerId = wrongOperator.Passengers[0].Id,
                        OriginalSeatNumber = "C01",
                        NewSeatNumber = "D01",
                        OriginalBoardingStatus = "PENDING",
                    },
                ]);
            await ConsumeAsync(dataSource, evt);

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, OccurredAt);
            var eligiblePersisted = await verify.Bookings
                .AsNoTracking()
                .Include(row => row.Passengers)
                .SingleAsync(row => row.Id == eligible.Id);
            eligiblePersisted.TripId.Should().Be(evt.NewTripId);
            eligiblePersisted.Status.Should().Be(BookingStatus.PARTIAL_NO_SHOW);
            eligiblePersisted.Passengers.Single(row => row.Id == eligible.Passengers[0].Id)
                .SeatNumber.Should().BeNull();
            eligiblePersisted.Passengers.Single(row => row.Id == eligible.Passengers[1].Id)
                .Should().Match<Passenger>(row =>
                    row.SeatNumber == "A02"
                    && row.BoardingStatus == PassengerBoardingStatus.NO_SHOW);

            var ineligiblePersisted = await verify.Bookings.AsNoTracking()
                .SingleAsync(row => row.Id == wrongOperator.Id);
            ineligiblePersisted.TripId.Should().Be(oldTripId);
            var transfer = await verify.BookingTransfers.AsNoTracking().SingleAsync();
            transfer.PassengerId.Should().Be(eligible.Passengers[0].Id);
            transfer.OriginalSeatNumber.Should().Be("A01");
            transfer.NewSeatNumber.Should().BeNull();
            (await verify.BookingPendingActions.CountAsync()).Should().Be(0);
            (await verify.OutboxEvents.CountAsync(row =>
                row.EventType == "booking.booking.transferred")).Should().Be(1);
            var shortage = await verify.OutboxEvents.AsNoTracking().SingleAsync(row =>
                row.EventType == "booking.booking.seat_shortage_detected");
            using var shortagePayload = JsonDocument.Parse(shortage.Payload);
            shortagePayload.RootElement.GetProperty("affectedPassengerCount").GetInt32()
                .Should().Be(1);
            shortagePayload.RootElement.GetProperty("bookingId").GetGuid()
                .Should().Be(eligible.Id);
        });
    }

    [Fact]
    public async Task ChainedSubstitutionPersistsNullOriginalAndNewSeatsWithoutBlockingOrSentinel()
    {
        await WithDatabaseAsync(async (dataSource, oldTripId, operatorId) =>
        {
            var booking = CreateConfirmedBooking(oldTripId, operatorId, "A01");
            await SeedAsync(dataSource, booking);
            var first = CreateEvent(
                oldTripId,
                operatorId,
                booking,
                (booking.Passengers[0], null, "PENDING"));
            await ConsumeAsync(dataSource, first);

            var chained = CreateEvent(
                first.NewTripId,
                operatorId,
                booking,
                [(booking.Passengers[0], null, "PENDING")],
                originalSeats: [null]);
            await ConsumeAsync(dataSource, chained);

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, OccurredAt);
            var persisted = await verify.Bookings
                .AsNoTracking()
                .Include(row => row.Passengers)
                .SingleAsync();
            persisted.TripId.Should().Be(chained.NewTripId);
            persisted.Passengers.Should().ContainSingle().Which.SeatNumber.Should().BeNull();
            var transfers = await verify.BookingTransfers.AsNoTracking()
                .ToArrayAsync();
            transfers.Should().HaveCount(2);
            transfers.Single(row => row.OriginalTripId == oldTripId)
                .NewSeatNumber.Should().BeNull();
            var chainedTransfer = transfers.Single(row => row.OriginalTripId == first.NewTripId);
            chainedTransfer.OriginalSeatNumber.Should().BeNull();
            chainedTransfer.NewSeatNumber.Should().BeNull();
            transfers.Should().OnlyContain(row => row.ConfirmationStatus
                == BookingTransferConfirmationStatus.NOT_REQUIRED);
        });
    }

    [Fact]
    public async Task LegacyPersistedBookingCodeDoesNotNackVehicleSubstitution()
    {
        await WithDatabaseAsync(async (dataSource, oldTripId, operatorId) =>
        {
            var booking = CreateConfirmedBooking(oldTripId, operatorId, "A01");
            await SeedAsync(dataSource, booking);
            await using (var command = dataSource.CreateCommand("""
                UPDATE vietride_booking.bookings
                SET booking_code = 'VR-D40-ACTIVE'
                WHERE id = @booking_id;
                """))
            {
                command.Parameters.AddWithValue("booking_id", booking.Id);
                await command.ExecuteNonQueryAsync();
            }

            var evt = CreateEvent(
                oldTripId,
                operatorId,
                booking,
                (booking.Passengers[0], "B01", "PENDING"));

            var result = await ConsumeAsync(dataSource, evt);

            result.Should().Be(IntegrationEventInboxResult.Processed);
            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, OccurredAt);
            var persisted = await verify.Bookings.AsNoTracking().SingleAsync();
            persisted.BookingCode.Value.Should().Be("VR-D40-ACTIVE");
            persisted.TripId.Should().Be(evt.NewTripId);
            (await verify.BookingTransfers.CountAsync()).Should().Be(1);
        });
    }

    [Fact]
    public async Task DuplicateAndInjectedFailureAreAtomicAcrossInboxStateTransfersAndOutbox()
    {
        await WithDatabaseAsync(async (dataSource, oldTripId, operatorId) =>
        {
            var committed = CreateConfirmedBooking(oldTripId, operatorId, "A01");
            var rollback = CreateConfirmedBooking(oldTripId, operatorId, "A02");
            await SeedAsync(dataSource, committed, rollback);
            var committedEvent = CreateEvent(
                oldTripId,
                operatorId,
                committed,
                (committed.Passengers[0], "B01", "PENDING"));

            var first = await ConsumeAsync(dataSource, committedEvent);
            var duplicate = await ConsumeAsync(dataSource, committedEvent);
            first.Should().Be(IntegrationEventInboxResult.Processed);
            duplicate.Should().Be(IntegrationEventInboxResult.Duplicate);

            var rollbackEvent = CreateEvent(
                oldTripId,
                operatorId,
                rollback,
                (rollback.Passengers[0], "B02", "PENDING"));
            var act = () => ConsumeAsync(dataSource, rollbackEvent, failAfterHandler: true);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Injected failure after handler flush.");

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, OccurredAt);
            (await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == committed.Id))
                .TripId.Should().Be(committedEvent.NewTripId);
            (await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == rollback.Id))
                .TripId.Should().Be(oldTripId);
            (await verify.BookingTransfers.CountAsync()).Should().Be(1);
            (await verify.OutboxEvents.CountAsync(row =>
                row.EventType == "booking.booking.transferred")).Should().Be(1);
            var inbox = await verify.Set<IntegrationInboxRecord>().AsNoTracking().ToArrayAsync();
            inbox.Should().ContainSingle(row => row.MessageId == committedEvent.EventId);
            inbox.Should().NotContain(row => row.MessageId == rollbackEvent.EventId);
        });
    }

    [Fact]
    public async Task EmptyCanonicalMappingSetIsProcessedAsInboxNoOp()
    {
        await WithDatabaseAsync(async (dataSource, oldTripId, operatorId) =>
        {
            var booking = CreateConfirmedBooking(oldTripId, operatorId, "A01");
            await SeedAsync(dataSource, booking);
            var evt = CreateEvent(
                oldTripId,
                operatorId,
                booking,
                (booking.Passengers[0], "B01", "PENDING")) with
            {
                Mappings = [],
            };

            var result = await ConsumeAsync(dataSource, evt);

            result.Should().Be(IntegrationEventInboxResult.Processed);
            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, OccurredAt);
            var persisted = await verify.Bookings
                .AsNoTracking()
                .Include(row => row.Passengers)
                .SingleAsync();
            persisted.TripId.Should().Be(oldTripId);
            persisted.Status.Should().Be(BookingStatus.CONFIRMED);
            persisted.Passengers.Should().ContainSingle().Which.Should().Match<Passenger>(
                passenger => passenger.SeatNumber == "A01"
                    && passenger.BoardingStatus == PassengerBoardingStatus.PENDING);
            (await verify.BookingTransfers.CountAsync()).Should().Be(0);
            (await verify.OutboxEvents.CountAsync()).Should().Be(0);
            (await verify.Set<IntegrationInboxRecord>().AsNoTracking().ToArrayAsync())
                .Should().ContainSingle(row =>
                    row.ConsumerName == "booking.trip-vehicle-substituted"
                    && row.MessageId == evt.EventId);
        });
    }

    [Fact]
    public void RegistrationUsesCanonicalBindingAndGenericInboxTransaction()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trip:UseDevStub"] = "true",
                ["Identity:UseDevStub"] = "true",
                ["Payment:UseDevStub"] = "true",
                ["REDIS_URL"] = "localhost:6379",
                ["RabbitMq:ExchangeName"] = "vietride.events",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVietRideMessaging(configuration);
        services.AddInfrastructure(configuration, registerConsumers: true);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<RabbitMqConsumerOptions<TripVehicleSubstitutedIntegrationEvent>>>()
            .Value.Value;
        options.QueueName.Should().Be("booking.trip-vehicle-substituted");
        options.BindingKeys.Should().Equal("trip.trip.vehicle_substituted");
        typeof(TripVehicleSubstitutedIntegrationEvent).Assembly
            .GetType(
                "VietRide.Booking.Infrastructure.Messaging.TripVehicleSubstitutedIntegrationEventHandler",
                throwOnError: true)!
            .GetConstructors()
            .Should().ContainSingle()
            .Which.GetParameters()
            .Should().ContainSingle(parameter => parameter.ParameterType == typeof(IMediator));
    }

    internal static async Task<IntegrationEventInboxResult> ConsumeAsync(
        Npgsql.NpgsqlDataSource dataSource,
        TripVehicleSubstitutedIntegrationEvent evt,
        bool failAfterHandler = false)
    {
        await using var db = Day22EventDatabase.CreateDbContext(dataSource, OccurredAt);
        var handler = CreateDatabaseBackedHandler(db);
        var inbox = new EfIntegrationEventInbox<BookingDbContext>(
            db,
            new EfUnitOfWork(db),
            new FixedClock(OccurredAt));
        return await inbox.ExecuteAsync(
            "booking.trip-vehicle-substituted",
            evt.EventId,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(evt))),
            async cancellationToken =>
            {
                await handler.HandleAsync(evt, cancellationToken);
                if (failAfterHandler)
                    throw new InvalidOperationException("Injected failure after handler flush.");
            },
            CancellationToken.None);
    }

    internal static TripVehicleSubstitutedIntegrationEvent CreateEvent(
        Guid oldTripId,
        Guid operatorId,
        BookingEntity booking,
        params (Passenger Passenger, string? NewSeat, string BoardingStatus)[] mappings)
        => CreateEvent(oldTripId, operatorId, booking, mappings, null, null);

    internal static TripVehicleSubstitutedIntegrationEvent CreateEvent(
        Guid oldTripId,
        Guid operatorId,
        BookingEntity booking,
        (Passenger Passenger, string? NewSeat, string BoardingStatus)[] mappings,
        IReadOnlyCollection<TripVehicleSubstitutedMapping>? additionalMappings = null,
        IReadOnlyList<string?>? originalSeats = null)
    {
        var eventId = Guid.NewGuid();
        return new TripVehicleSubstitutedIntegrationEvent
        {
            EventId = eventId,
            OccurredAt = OccurredAt.UtcDateTime,
            SubstitutionId = eventId,
            DisruptedAt = OccurredAt,
            OperatorId = operatorId,
            OldTripId = oldTripId,
            OldTripStatus = "DISRUPTED",
            OldVehicleId = Guid.NewGuid(),
            NewTripId = Guid.NewGuid(),
            NewTripStatus = "BOARDING",
            NewVehicleId = Guid.NewGuid(),
            NewVehiclePlateNumber = "51B-999.99",
            NewTripDepartureDateTime = OccurredAt.AddMinutes(30),
            ActorUserId = Guid.NewGuid(),
            Reason = "Safety replacement",
            NotifyPassengers = false,
            Mappings = mappings.Select((mapping, index) => new TripVehicleSubstitutedMapping
            {
                BookingId = booking.Id,
                PassengerId = mapping.Passenger.Id,
                OriginalSeatNumber = originalSeats is null
                    ? mapping.Passenger.SeatNumber
                    : originalSeats[index],
                NewSeatNumber = mapping.NewSeat,
                OriginalBoardingStatus = mapping.BoardingStatus,
                OriginalSeatType = null,
                NewSeatType = null,
                IsSeatDowngrade = false,
            }).Concat(additionalMappings ?? []).ToArray(),
        };
    }

    internal static BookingEntity CreateConfirmedBooking(
        Guid tripId,
        Guid operatorId,
        params string[] seats)
    {
        var booking = Day22EventDatabase.CreateBooking(
            tripId,
            operatorId,
            confirmed: false,
            totalAmount: seats.Length * 100_000L);
        foreach (var seat in seats)
        {
            booking.AddTicketedPassenger(
                seat,
                VietRide.Booking.Domain.ValueObjects.TicketCode.Generate(DateTimeOffset.UtcNow),
                Money.FromRaw(100_000),
                Money.Zero,
                Money.FromRaw(100_000));
        }
        booking.Confirm(OccurredAt.AddDays(-1));
        return booking;
    }

    internal static async Task SeedAsync(
        Npgsql.NpgsqlDataSource dataSource,
        params BookingEntity[] bookings)
    {
        await using var seed = Day22EventDatabase.CreateDbContext(dataSource, OccurredAt);
        await seed.Database.MigrateAsync();
        seed.Bookings.AddRange(bookings);
        await seed.SaveChangesAsync();
    }

    internal static async Task WithDatabaseAsync(
        Func<Npgsql.NpgsqlDataSource, Guid, Guid, Task> test)
    {
        var databaseName = $"vr_d34_substitution_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);
        try
        {
            await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
            await test(dataSource, Guid.NewGuid(), Guid.NewGuid());
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static IIntegrationEventHandler<TripVehicleSubstitutedIntegrationEvent>
        CreateDatabaseBackedHandler(BookingDbContext db)
    {
        var commandHandler = new ApplyVehicleSubstitutionCommandHandler(
            Day22EventDatabase.CreateBookingRepository(db),
            CreateTransferRepository(db),
            new IntegrationEventOutbox(new OutboxStore(db, new FixedClock(OccurredAt))),
            new EfUnitOfWork(db),
            new FixedClock(OccurredAt));
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ApplyVehicleSubstitutionCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => commandHandler.Handle(
                call.Arg<ApplyVehicleSubstitutionCommand>(),
                call.Arg<CancellationToken>()));
        var type = typeof(TripVehicleSubstitutedIntegrationEvent).Assembly.GetType(
            "VietRide.Booking.Infrastructure.Messaging.TripVehicleSubstitutedIntegrationEventHandler",
            throwOnError: true)!;
        return (IIntegrationEventHandler<TripVehicleSubstitutedIntegrationEvent>)
            Activator.CreateInstance(type, mediator)!;
    }

    private static IBookingTransferRepository CreateTransferRepository(BookingDbContext db)
        => (IBookingTransferRepository)Activator.CreateInstance(
            typeof(BookingDbContext).Assembly.GetType(
                "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingTransferRepository",
                throwOnError: true)!,
            db)!;
}
