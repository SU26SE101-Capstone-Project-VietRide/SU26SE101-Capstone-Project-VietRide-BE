using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Npgsql.NameTranslation;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.Bookings.CancelBooking;
using VietRide.Booking.Application.Features.Bookings.ConfirmBookingOnPayment;
using VietRide.Booking.Application.Features.Bookings.ExpireBookingOnPayment;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Messaging.RabbitMq;
using VietRide.Shared.Persistence.Inbox;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests;

public sealed class BookingPaymentRefundRequestedIntegrationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-31T12:00:00Z");

    [Fact]
    public async Task LateRoundTripCapture_EmitsOneExactRefundAllocationPerBooking()
    {
        var groupId = Guid.NewGuid();
        var first = CreateBooking(groupId, 200_000);
        var second = CreateBooking(groupId, 300_000);
        var bookings = Substitute.For<IBookingRepository>();
        bookings.QueryNoTracking().Returns(new[] { first, second }.AsQueryable());
        bookings.TryExpirePendingPaymentAsync(
                Arg.Any<Guid>(),
                Now,
                Arg.Any<CancellationToken>())
            .Returns(true);
        var payloads = new List<string>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        outbox.When(call => call.EnqueueAsync(
                "booking.payment_refund.requested",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()))
            .Do(call => payloads.Add(call.ArgAt<string>(1)));
        var handler = new ConfirmBookingOnPaymentCommandHandler(
            bookings,
            Substitute.For<ITripServiceClient>(),
            outbox,
            new FixedClock(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
            Substitute.For<IBookingStatusHistoryRepository>());

        var changed = await handler.Handle(
            new ConfirmBookingOnPaymentCommand(
                Guid.NewGuid(),
                "BOOKING_GROUP",
                groupId,
                500_000,
                "VNPAY",
                Now,
                Now),
            CancellationToken.None);

        changed.Should().BeTrue();
        payloads.Should().HaveCount(2);
        payloads.Select(payload =>
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.GetProperty("bookingId").GetGuid();
        }).Should().BeEquivalentTo([first.Id, second.Id]);
        payloads.Sum(payload =>
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.GetProperty("amount").GetInt64();
        }).Should().Be(500_000);
    }

    [Fact]
    public async Task LateCapture_AfterBookingAlreadyConfirmed_EmitsExactRefundWithoutReopening()
    {
        var booking = CreateBooking();
        booking.Confirm(Now.AddMinutes(-5));
        var bookings = Substitute.For<IBookingRepository>();
        bookings.QueryNoTracking().Returns(new[] { booking }.AsQueryable());
        var trip = Substitute.For<ITripServiceClient>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new ConfirmBookingOnPaymentCommandHandler(
            bookings,
            trip,
            outbox,
            new FixedClock(),
            NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
            Substitute.For<IBookingStatusHistoryRepository>());
        var paymentId = Guid.NewGuid();

        var changed = await handler.Handle(
            new ConfirmBookingOnPaymentCommand(
                paymentId,
                "BOOKING",
                booking.Id,
                booking.TotalAmount.Amount,
                "VNPAY",
                Now,
                Now),
            CancellationToken.None);

        changed.Should().BeFalse();
        booking.Status.Should().Be(BookingStatus.CONFIRMED);
        await bookings.DidNotReceiveWithAnyArgs()
            .TryExpirePendingPaymentAsync(default, default, default);
        await trip.DidNotReceiveWithAnyArgs()
            .ConfirmBookedSeatsAsync(default, default, default, default!, default);
        await outbox.Received(1).EnqueueAsync(
            "booking.payment_refund.requested",
            Arg.Is<string>(payload =>
                payload.Contains(paymentId.ToString(), StringComparison.OrdinalIgnoreCase)
                && payload.Contains(booking.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                && payload.Contains("PAYMENT_CAPTURE_AFTER_BOOKING_EXPIRY", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LateCapture_AfterRoundTripAlreadyConfirmed_EmitsOneRefundPerAllocation()
    {
        var groupId = Guid.NewGuid();
        var first = CreateBooking(groupId, 200_000);
        var second = CreateBooking(groupId, 300_000);
        first.Confirm(Now.AddMinutes(-5));
        second.Confirm(Now.AddMinutes(-5));
        var bookings = Substitute.For<IBookingRepository>();
        bookings.QueryNoTracking().Returns(new[] { first, second }.AsQueryable());
        var trip = Substitute.For<ITripServiceClient>();
        var payloads = new List<string>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        outbox.When(call => call.EnqueueAsync(
                "booking.payment_refund.requested",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()))
            .Do(call => payloads.Add(call.ArgAt<string>(1)));
        var handler = new ConfirmBookingOnPaymentCommandHandler(
            bookings,
            trip,
            outbox,
            new FixedClock(),
            NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
            Substitute.For<IBookingStatusHistoryRepository>());

        var changed = await handler.Handle(
            new ConfirmBookingOnPaymentCommand(
                Guid.NewGuid(),
                "BOOKING_GROUP",
                groupId,
                500_000,
                "VNPAY",
                Now,
                Now),
            CancellationToken.None);

        changed.Should().BeFalse();
        first.Status.Should().Be(BookingStatus.CONFIRMED);
        second.Status.Should().Be(BookingStatus.CONFIRMED);
        payloads.Should().HaveCount(2);
        payloads.Sum(payload =>
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.GetProperty("amount").GetInt64();
        }).Should().Be(500_000);
        await trip.DidNotReceiveWithAnyArgs()
            .ConfirmBookedRoundTripSeatsAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task DefinitiveSeatLoss_ExpiresAndEmitsRefundRequest()
    {
        var booking = CreateBooking();
        var bookings = Substitute.For<IBookingRepository>();
        bookings.QueryNoTracking().Returns(new[] { booking }.AsQueryable());
        bookings.GetPendingPaymentTransitionSnapshotAsync(
                booking.Id,
                Arg.Any<CancellationToken>())
            .Returns(CreateSnapshot(booking));
        bookings.TryExpirePendingPaymentAsync(
                booking.Id,
                Now,
                Arg.Any<CancellationToken>())
            .Returns(true);
        var trip = Substitute.For<ITripServiceClient>();
        trip.ConfirmBookedSeatsAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                booking.Id,
                Arg.Any<IReadOnlyList<PassengerSeatAssignment>>(),
                Arg.Any<CancellationToken>())
            .Returns(new SeatConfirmationOutcome.DefinitiveSeatUnavailable("expired lock"));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new ConfirmBookingOnPaymentCommandHandler(
            bookings,
            trip,
            outbox,
            new FixedClock(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
            Substitute.For<IBookingStatusHistoryRepository>());

        var changed = await handler.Handle(
            new ConfirmBookingOnPaymentCommand(
                Guid.NewGuid(),
                "BOOKING",
                booking.Id,
                booking.TotalAmount.Amount,
                "VNPAY",
                Now.AddMinutes(-1),
                Now.AddMinutes(1)),
            CancellationToken.None);

        changed.Should().BeTrue();
        await outbox.Received(1).EnqueueAsync(
            "booking.payment_refund.requested",
            Arg.Is<string>(payload =>
                payload.Contains("SEAT_CONFIRMATION_FAILED", StringComparison.Ordinal)
                && payload.Contains(booking.Id.ToString(), StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransientTripFailure_ConsumerOnlyAcksSafelyRoutedRetryWithoutExpiringOrRefunding(
        bool retryPublishReturned)
    {
        var booking = CreateBooking();
        var bookings = Substitute.For<IBookingRepository>();
        bookings.QueryNoTracking().Returns(new[] { booking }.AsQueryable());
        bookings.GetPendingPaymentTransitionSnapshotAsync(
                booking.Id,
                Arg.Any<CancellationToken>())
            .Returns(CreateSnapshot(booking));
        var trip = Substitute.For<ITripServiceClient>();
        trip.ConfirmBookedSeatsAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                booking.Id,
                Arg.Any<IReadOnlyList<PassengerSeatAssignment>>(),
                Arg.Any<CancellationToken>())
            .Returns(new SeatConfirmationOutcome.TransientFailure("HTTP 503"));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var commandHandler = new ConfirmBookingOnPaymentCommandHandler(
            bookings,
            trip,
            outbox,
            new FixedClock(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
            Substitute.For<IBookingStatusHistoryRepository>());

        var paymentEvent = new PaymentSucceededIntegrationEvent
        {
            PaymentId = Guid.NewGuid(),
            ReferenceType = "BOOKING",
            ReferenceId = booking.Id,
            Amount = booking.TotalAmount.Amount,
            Method = "VNPAY",
            PaidAt = Now.AddMinutes(-1),
            DueAt = Now.AddMinutes(1),
        };
        var services = new ServiceCollection();
        services.AddScoped<IIntegrationEventHandler<PaymentSucceededIntegrationEvent>>(
            _ => new DelegatingPaymentSucceededHandler(commandHandler));
        services.AddScoped<IIntegrationEventInbox, PassThroughIntegrationEventInbox>();
        using var provider = services.BuildServiceProvider();
        var retryPublisher = Substitute.For<IModel>();
        var retryProperties = Substitute.For<IBasicProperties>();
        retryPublisher.CreateBasicProperties().Returns(retryProperties);
        var connection = Substitute.For<IConnection>();
        connection.CreateModel().Returns(retryPublisher);
        var connections = Substitute.For<IRabbitMqConnectionFactory>();
        connections.GetOrCreate().Returns(connection);
        var consumer = new RabbitMqConsumerBackgroundService<PaymentSucceededIntegrationEvent>(
            connections,
            Options.Create(new RabbitMqOptions()),
            Options.Create(new RabbitMqConsumerOptions<PaymentSucceededIntegrationEvent>
            {
                Value = new RabbitMqConsumerOptions
                {
                    QueueName = "booking.payment-succeeded",
                    BindingKeys = [PaymentSucceededIntegrationEvent.EventType],
                    TransientRetryCount = 5,
                    TransientRetryDelay = TimeSpan.FromSeconds(10),
                },
            }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RabbitMqConsumerBackgroundService<PaymentSucceededIntegrationEvent>>.Instance);
        var channel = Substitute.For<IModel>();
        if (retryPublishReturned)
        {
            retryPublisher
                .When(candidate => candidate.BasicPublish(
                    "booking.payment-succeeded.retry.dlx",
                    "booking.payment-succeeded.retry",
                    true,
                    retryProperties,
                    Arg.Any<ReadOnlyMemory<byte>>()))
                .Do(call =>
                {
                    retryPublisher.BasicReturn += Raise.EventWith(new BasicReturnEventArgs
                    {
                        ReplyCode = 312,
                        ReplyText = "NO_ROUTE",
                        Exchange = "booking.payment-succeeded.retry.dlx",
                        RoutingKey = "booking.payment-succeeded.retry",
                        BasicProperties = retryProperties,
                        Body = call.ArgAt<ReadOnlyMemory<byte>>(4),
                    });
                });
        }

        await consumer.ProcessDeliveryAsync(
            channel,
            CreatePaymentSucceededDelivery(49, paymentEvent),
            CancellationToken.None);

        if (retryPublishReturned)
        {
            Received.InOrder(() =>
            {
                retryPublisher.ConfirmSelect();
                retryPublisher.BasicPublish(
                    "booking.payment-succeeded.retry.dlx",
                    "booking.payment-succeeded.retry",
                    true,
                    retryProperties,
                    Arg.Any<ReadOnlyMemory<byte>>());
                retryPublisher.WaitForConfirmsOrDie(Arg.Any<TimeSpan>());
                channel.BasicNack(49, multiple: false, requeue: true);
            });
            channel.DidNotReceive().BasicAck(Arg.Any<ulong>(), Arg.Any<bool>());
        }
        else
        {
            Received.InOrder(() =>
            {
                retryPublisher.ConfirmSelect();
                retryPublisher.BasicPublish(
                    "booking.payment-succeeded.retry.dlx",
                    "booking.payment-succeeded.retry",
                    true,
                    retryProperties,
                    Arg.Any<ReadOnlyMemory<byte>>());
                retryPublisher.WaitForConfirmsOrDie(Arg.Any<TimeSpan>());
                channel.BasicAck(49, multiple: false);
            });
            channel.DidNotReceive().BasicNack(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<bool>());
        }

        retryProperties.MessageId.Should().Be(paymentEvent.EventId.ToString("D"));
        retryProperties.DeliveryMode.Should().Be(2);
        retryProperties.Headers.Should().Contain("vietride-retry-count", 1L);
        await bookings.DidNotReceiveWithAnyArgs()
            .TryExpirePendingPaymentAsync(default, default, default);
        await outbox.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(default!, default!, default);
    }

    [Fact]
    public async Task LateCapture_CommitsBookingOutboxAndInboxAtomically_AndReplayIsNoOp()
    {
        var databaseName = $"vietride_booking_payment_refund_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            await using var db = CreateDbContext(dataSource);
            await db.Database.MigrateAsync();
            var booking = CreateBooking();
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var clock = new FixedClock();
            var unitOfWork = new EfUnitOfWork(db);
            var outbox = new IntegrationEventOutbox(new OutboxStore(db, clock));
            var handler = CreateHandler(db, outbox, clock);
            var inbox = new EfIntegrationEventInbox<BookingDbContext>(db, unitOfWork, clock);
            var paymentId = Guid.NewGuid();
            var command = new ConfirmBookingOnPaymentCommand(
                paymentId,
                "BOOKING",
                booking.Id,
                booking.TotalAmount.Amount,
                "VNPAY",
                Now,
                Now);

            var first = await inbox.ExecuteAsync(
                "booking.payment-succeeded",
                paymentId,
                new string('a', 64),
                ct => ExecuteAsync(handler, command, ct),
                CancellationToken.None);
            db.ChangeTracker.Clear();
            var duplicate = await inbox.ExecuteAsync(
                "booking.payment-succeeded",
                paymentId,
                new string('a', 64),
                ct => ExecuteAsync(handler, command, ct),
                CancellationToken.None);

            first.Should().Be(IntegrationEventInboxResult.Processed);
            duplicate.Should().Be(IntegrationEventInboxResult.Duplicate);
            (await db.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id))
                .Status.Should().Be(BookingStatus.EXPIRED);
            (await db.BookingStatusHistories.AsNoTracking()
                .CountAsync(row => row.BookingId == booking.Id)).Should().Be(1);
            var refundOutbox = await db.OutboxEvents.AsNoTracking()
                .Where(row => row.EventType == "booking.payment_refund.requested")
                .ToListAsync();
            refundOutbox.Should().ContainSingle();
            using var payload = JsonDocument.Parse(refundOutbox[0].Payload);
            payload.RootElement.EnumerateObject().Select(property => property.Name)
                .Should().BeEquivalentTo(
                    "eventId",
                    "occurredAt",
                    "paymentId",
                    "paymentReferenceType",
                    "paymentReferenceId",
                    "bookingId",
                    "userId",
                    "amount",
                    "reason");
            payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(refundOutbox[0].Id);
            payload.RootElement.TryGetProperty("eventType", out _).Should().BeFalse();
            await AssertPublisherRestartAsync(refundOutbox[0]);
            (await db.Set<IntegrationInboxRecord>().AsNoTracking()
                .CountAsync(row => row.MessageId == paymentId)).Should().Be(1);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task LegacyNullDeadlineFallback_LateDistinctCaptureOnConfirmedBooking_RefundsWithoutReopening()
    {
        var databaseName = $"vietride_booking_legacy_deadline_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            await using var db = CreateDbContext(dataSource);
            await db.Database.MigrateAsync();
            var booking = CreateBooking();
            booking.Confirm(Now.AddMinutes(-20));
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var clock = new FixedClock();
            var unitOfWork = new EfUnitOfWork(db);
            var outbox = new IntegrationEventOutbox(new OutboxStore(db, clock));
            var handler = CreateHandler(db, outbox, clock);
            var inbox = new EfIntegrationEventInbox<BookingDbContext>(db, unitOfWork, clock);
            var distinctPaymentId = Guid.NewGuid();
            var effectiveLegacyDueAt = Now.AddMinutes(-5);
            var command = new ConfirmBookingOnPaymentCommand(
                distinctPaymentId,
                "BOOKING",
                booking.Id,
                booking.TotalAmount.Amount,
                "VNPAY",
                Now,
                effectiveLegacyDueAt);

            var first = await inbox.ExecuteAsync(
                "booking.payment-succeeded",
                distinctPaymentId,
                new string('b', 64),
                ct => ExecuteAsync(handler, command, ct),
                CancellationToken.None);
            db.ChangeTracker.Clear();
            var duplicate = await inbox.ExecuteAsync(
                "booking.payment-succeeded",
                distinctPaymentId,
                new string('b', 64),
                ct => ExecuteAsync(handler, command, ct),
                CancellationToken.None);

            first.Should().Be(IntegrationEventInboxResult.Processed);
            duplicate.Should().Be(IntegrationEventInboxResult.Duplicate);
            (await db.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id))
                .Status.Should().Be(BookingStatus.CONFIRMED);
            (await db.BookingStatusHistories.AsNoTracking()
                .CountAsync(row => row.BookingId == booking.Id)).Should().Be(0);
            var refund = await db.OutboxEvents.AsNoTracking()
                .SingleAsync(row => row.EventType == "booking.payment_refund.requested");
            using var payload = JsonDocument.Parse(refund.Payload);
            payload.RootElement.GetProperty("paymentId").GetGuid().Should().Be(distinctPaymentId);
            payload.RootElement.GetProperty("bookingId").GetGuid().Should().Be(booking.Id);
            payload.RootElement.GetProperty("amount").GetInt64()
                .Should().Be(booking.TotalAmount.Amount);
            payload.RootElement.GetProperty("reason").GetString()
                .Should().Be("PAYMENT_CAPTURE_AFTER_BOOKING_EXPIRY");
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task OnTimeCapture_AfterCancellation_PreservesBookingAndTicket_AndReplayEmitsOneExactRefund()
    {
        var databaseName = $"vietride_booking_cancelled_late_capture_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            await using var db = CreateDbContext(dataSource);
            await db.Database.MigrateAsync();
            var booking = CreateBooking();
            var ticket = booking.AddTicketedPassenger(
                "A01",
                TicketCode.Generate(Now),
                booking.TotalAmount,
                Money.Zero,
                booking.TotalAmount);
            booking.Cancel(
                BookingCancellationReason.USER_INITIATED,
                Now.AddMinutes(-10));
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var clock = new FixedClock();
            var unitOfWork = new EfUnitOfWork(db);
            var outbox = new IntegrationEventOutbox(new OutboxStore(db, clock));
            var handler = CreateHandler(db, outbox, clock);
            var inbox = new EfIntegrationEventInbox<BookingDbContext>(db, unitOfWork, clock);
            var paymentId = Guid.NewGuid();
            var command = new ConfirmBookingOnPaymentCommand(
                paymentId,
                "BOOKING",
                booking.Id,
                booking.TotalAmount.Amount,
                "VNPAY",
                Now.AddMinutes(-1),
                Now.AddMinutes(1));

            var first = await inbox.ExecuteAsync(
                "booking.payment-succeeded",
                paymentId,
                new string('c', 64),
                ct => ExecuteAsync(handler, command, ct),
                CancellationToken.None);
            db.ChangeTracker.Clear();
            var duplicate = await inbox.ExecuteAsync(
                "booking.payment-succeeded",
                paymentId,
                new string('c', 64),
                ct => ExecuteAsync(handler, command, ct),
                CancellationToken.None);

            first.Should().Be(IntegrationEventInboxResult.Processed);
            duplicate.Should().Be(IntegrationEventInboxResult.Duplicate);
            (await db.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id))
                .Status.Should().Be(BookingStatus.CANCELLED);
            (await db.Tickets.AsNoTracking().SingleAsync(row => row.Id == ticket.Id))
                .Status.Should().Be(TicketStatus.CANCELLED);
            (await db.BookingStatusHistories.AsNoTracking()
                .CountAsync(row => row.BookingId == booking.Id)).Should().Be(0);
            var refundOutbox = await db.OutboxEvents.AsNoTracking()
                .Where(row => row.EventType == "booking.payment_refund.requested")
                .ToListAsync();
            refundOutbox.Should().ContainSingle();
            using var payload = JsonDocument.Parse(refundOutbox[0].Payload);
            payload.RootElement.GetProperty("paymentId").GetGuid().Should().Be(paymentId);
            payload.RootElement.GetProperty("bookingId").GetGuid().Should().Be(booking.Id);
            payload.RootElement.GetProperty("amount").GetInt64()
                .Should().Be(booking.TotalAmount.Amount);
            payload.RootElement.GetProperty("reason").GetString()
                .Should().Be("SEAT_CONFIRMATION_FAILED");
            (await db.Set<IntegrationInboxRecord>().AsNoTracking()
                .CountAsync(row => row.MessageId == paymentId)).Should().Be(1);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task PaymentFirstThenCancel_SerializesToConfirmationThenNormalCancellationWithoutExactRefund()
    {
        var databaseName = $"vietride_booking_payment_cancel_race_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var booking = CreateBooking();
            var ticket = booking.AddTicketedPassenger(
                "A01",
                TicketCode.Generate(Now),
                booking.TotalAmount,
                Money.Zero,
                booking.TotalAmount);
            await using (var setup = CreateDbContext(dataSource))
            {
                await setup.Database.MigrateAsync();
                setup.Bookings.Add(booking);
                await setup.SaveChangesAsync();
            }

            await using var confirmationDb = CreateDbContext(dataSource);
            await using var cancellationDb = CreateDbContext(dataSource);
            var confirmationEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseConfirmation = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var confirmationTrip = Substitute.For<ITripServiceClient>();
            confirmationTrip.ConfirmBookedSeatsAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    booking.Id,
                    Arg.Any<IReadOnlyList<PassengerSeatAssignment>>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => AwaitConfirmationAsync(
                    confirmationEntered,
                    releaseConfirmation));
            var paymentId = Guid.NewGuid();
            var confirmation = new ConfirmBookingOnPaymentCommandHandler(
                CreateBookingRepository(confirmationDb),
                confirmationTrip,
                new IntegrationEventOutbox(new OutboxStore(confirmationDb, new FixedClock())),
                new FixedClock(),
                NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
                CreateHistoryRepository(confirmationDb));
            var confirmationInbox = new EfIntegrationEventInbox<BookingDbContext>(
                confirmationDb,
                new EfUnitOfWork(confirmationDb),
                new FixedClock());
            var confirmationTask = confirmationInbox.ExecuteAsync(
                "booking.payment-succeeded",
                paymentId,
                new string('1', 64),
                ct => ExecuteAsync(
                    confirmation,
                    new ConfirmBookingOnPaymentCommand(
                        paymentId,
                        "BOOKING",
                        booking.Id,
                        booking.TotalAmount.Amount,
                        "VNPAY",
                        Now.AddMinutes(-1),
                        Now.AddMinutes(1)),
                    ct),
                CancellationToken.None);
            await confirmationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var cancellationTrip = Substitute.For<ITripServiceClient>();
            cancellationTrip.GetTripSnapshotAsync(
                    booking.TripId,
                    Arg.Any<CancellationToken>())
                .Returns(CreateTripSnapshot(booking));
            var cancellation = new CancelBookingCommandHandler(
                CreateBookingRepository(cancellationDb),
                cancellationTrip,
                Substitute.For<IOperatorServiceClient>(),
                new IntegrationEventOutbox(new OutboxStore(cancellationDb, new FixedClock())),
                new FixedClock(),
                NullLogger<CancelBookingCommandHandler>.Instance,
                CreateHistoryRepository(cancellationDb),
                Substitute.For<IBookingPendingActionRepository>());
            var cancellationUnitOfWork = new EfUnitOfWork(cancellationDb);
            var cancellationTask = cancellationUnitOfWork.ExecuteInTransactionAsync(
                () => cancellation.Handle(
                    new CancelBookingCommand(
                        booking.Id,
                        booking.PassengerUserId,
                        "cancel-after-payment",
                        "USER_INITIATED"),
                    CancellationToken.None),
                CancellationToken.None);

            await Task.Delay(150);
            cancellationTask.IsCompleted.Should().BeFalse();
            releaseConfirmation.SetResult();
            (await confirmationTask).Should().Be(IntegrationEventInboxResult.Processed);
            (await cancellationTask).Status.Should().Be("CANCELLED");

            await using var verify = CreateDbContext(dataSource);
            (await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id))
                .Status.Should().Be(BookingStatus.CANCELLED);
            (await verify.Tickets.AsNoTracking().SingleAsync(row => row.Id == ticket.Id))
                .Status.Should().Be(TicketStatus.CANCELLED);
            (await verify.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "booking.booking.confirmed")).Should().Be(1);
            (await verify.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "booking.booking.cancelled")).Should().Be(1);
            (await verify.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "booking.payment_refund.requested")).Should().Be(0);
            (await verify.Set<IntegrationInboxRecord>().AsNoTracking()
                .CountAsync(row => row.MessageId == paymentId)).Should().Be(1);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task CancelFirstThenPayment_SerializesToCancellationAndExactCapturedPaymentRefund()
    {
        var databaseName = $"vietride_booking_cancel_payment_race_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var booking = CreateBooking();
            var ticket = booking.AddTicketedPassenger(
                "A01",
                TicketCode.Generate(Now),
                booking.TotalAmount,
                Money.Zero,
                booking.TotalAmount);
            await using (var setup = CreateDbContext(dataSource))
            {
                await setup.Database.MigrateAsync();
                setup.Bookings.Add(booking);
                await setup.SaveChangesAsync();
            }

            await using var cancellationDb = CreateDbContext(dataSource);
            await using var confirmationDb = CreateDbContext(dataSource);
            var cancellationEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCancellation = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellationTrip = Substitute.For<ITripServiceClient>();
            cancellationTrip.GetTripSnapshotAsync(
                    booking.TripId,
                    Arg.Any<CancellationToken>())
                .Returns(_ => AwaitTripSnapshotAsync(
                    CreateTripSnapshot(booking),
                    cancellationEntered,
                    releaseCancellation));
            var cancellation = new CancelBookingCommandHandler(
                CreateBookingRepository(cancellationDb),
                cancellationTrip,
                Substitute.For<IOperatorServiceClient>(),
                new IntegrationEventOutbox(new OutboxStore(cancellationDb, new FixedClock())),
                new FixedClock(),
                NullLogger<CancelBookingCommandHandler>.Instance,
                CreateHistoryRepository(cancellationDb),
                Substitute.For<IBookingPendingActionRepository>());
            var cancellationUnitOfWork = new EfUnitOfWork(cancellationDb);
            var cancellationTask = cancellationUnitOfWork.ExecuteInTransactionAsync(
                () => cancellation.Handle(
                    new CancelBookingCommand(
                        booking.Id,
                        booking.PassengerUserId,
                        "cancel-before-payment",
                        "USER_INITIATED"),
                    CancellationToken.None),
                CancellationToken.None);
            await cancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var confirmationTrip = Substitute.For<ITripServiceClient>();
            var paymentId = Guid.NewGuid();
            var confirmation = new ConfirmBookingOnPaymentCommandHandler(
                CreateBookingRepository(confirmationDb),
                confirmationTrip,
                new IntegrationEventOutbox(new OutboxStore(confirmationDb, new FixedClock())),
                new FixedClock(),
                NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
                CreateHistoryRepository(confirmationDb));
            var confirmationInbox = new EfIntegrationEventInbox<BookingDbContext>(
                confirmationDb,
                new EfUnitOfWork(confirmationDb),
                new FixedClock());
            var confirmationTask = confirmationInbox.ExecuteAsync(
                "booking.payment-succeeded",
                paymentId,
                new string('3', 64),
                ct => ExecuteAsync(
                    confirmation,
                    new ConfirmBookingOnPaymentCommand(
                        paymentId,
                        "BOOKING",
                        booking.Id,
                        booking.TotalAmount.Amount,
                        "VNPAY",
                        Now.AddMinutes(-1),
                        Now.AddMinutes(1)),
                    ct),
                CancellationToken.None);

            await Task.Delay(150);
            confirmationTask.IsCompleted.Should().BeFalse();
            releaseCancellation.SetResult();
            (await cancellationTask).Status.Should().Be("CANCELLED");
            (await confirmationTask).Should().Be(IntegrationEventInboxResult.Processed);

            await using var verify = CreateDbContext(dataSource);
            (await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id))
                .Status.Should().Be(BookingStatus.CANCELLED);
            (await verify.Tickets.AsNoTracking().SingleAsync(row => row.Id == ticket.Id))
                .Status.Should().Be(TicketStatus.CANCELLED);
            (await verify.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "booking.booking.confirmed")).Should().Be(0);
            (await verify.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "booking.booking.cancelled")).Should().Be(1);
            var refund = await verify.OutboxEvents.AsNoTracking()
                .SingleAsync(row => row.EventType == "booking.payment_refund.requested");
            using var payload = JsonDocument.Parse(refund.Payload);
            payload.RootElement.GetProperty("paymentId").GetGuid().Should().Be(paymentId);
            payload.RootElement.GetProperty("reason").GetString()
                .Should().Be("SEAT_CONFIRMATION_FAILED");
            (await verify.Set<IntegrationInboxRecord>().AsNoTracking()
                .CountAsync(row => row.MessageId == paymentId)).Should().Be(1);
            await confirmationTrip.DidNotReceiveWithAnyArgs()
                .ConfirmBookedSeatsAsync(default, default, default, default!, default);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task GroupConfirmationCasLoss_DoesNotPartiallyConfirmAndRefundsEveryAllocation()
    {
        var databaseName = $"vietride_booking_group_cas_loss_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var groupId = Guid.NewGuid();
            var first = CreateBooking(groupId, 200_000);
            var second = CreateBooking(groupId, 300_000);
            var firstTicket = first.AddTicketedPassenger(
                "A01",
                TicketCode.Generate(Now),
                first.TotalAmount,
                Money.Zero,
                first.TotalAmount);
            var secondTicket = second.AddTicketedPassenger(
                "B01",
                TicketCode.Generate(Now.AddSeconds(1)),
                second.TotalAmount,
                Money.Zero,
                second.TotalAmount);
            await using (var setup = CreateDbContext(dataSource))
            {
                await setup.Database.MigrateAsync();
                setup.Bookings.AddRange(first, second);
                await setup.SaveChangesAsync();
            }

            await using var confirmationDb = CreateDbContext(dataSource);
            await using var competingDb = CreateDbContext(dataSource);
            var confirmationEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseConfirmation = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var trip = Substitute.For<ITripServiceClient>();
            trip.ConfirmBookedRoundTripSeatsAsync(
                    Arg.Any<RoundTripBookSeatsLeg>(),
                    Arg.Any<RoundTripBookSeatsLeg>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<Guid?>())
                .Returns(_ => AwaitConfirmationAsync(
                    confirmationEntered,
                    releaseConfirmation));
            var paymentId = Guid.NewGuid();
            var confirmation = new ConfirmBookingOnPaymentCommandHandler(
                CreateBookingRepository(confirmationDb),
                trip,
                new IntegrationEventOutbox(new OutboxStore(confirmationDb, new FixedClock())),
                new FixedClock(),
                NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
                CreateHistoryRepository(confirmationDb));
            var inbox = new EfIntegrationEventInbox<BookingDbContext>(
                confirmationDb,
                new EfUnitOfWork(confirmationDb),
                new FixedClock());
            var confirmationTask = inbox.ExecuteAsync(
                "booking.payment-succeeded",
                paymentId,
                new string('2', 64),
                ct => ExecuteAsync(
                    confirmation,
                    new ConfirmBookingOnPaymentCommand(
                        paymentId,
                        "BOOKING_GROUP",
                        groupId,
                        500_000,
                        "VNPAY",
                        Now.AddMinutes(-1),
                        Now.AddMinutes(1)),
                    ct),
                CancellationToken.None);
            await confirmationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var competingRepository = CreateBookingRepository(competingDb);
            var competingUnitOfWork = new EfUnitOfWork(competingDb);
            var cancelled = await competingUnitOfWork.ExecuteInTransactionAsync(
                () => competingRepository.TryCancelAsync(
                    second.Id,
                    BookingCancellationReason.USER_INITIATED,
                    Now,
                    refundOverride: false,
                    CancellationToken.None),
                CancellationToken.None);
            cancelled.Should().BeTrue();

            releaseConfirmation.SetResult();
            (await confirmationTask).Should().Be(IntegrationEventInboxResult.Processed);

            await using var verify = CreateDbContext(dataSource);
            var statuses = await verify.Bookings.AsNoTracking()
                .Where(booking => booking.BookingGroupId == groupId)
                .ToDictionaryAsync(booking => booking.Id, booking => booking.Status);
            statuses[first.Id].Should().Be(BookingStatus.EXPIRED);
            statuses[second.Id].Should().Be(BookingStatus.CANCELLED);
            (await verify.Tickets.AsNoTracking().SingleAsync(row => row.Id == firstTicket.Id))
                .Status.Should().Be(TicketStatus.EXPIRED);
            (await verify.Tickets.AsNoTracking().SingleAsync(row => row.Id == secondTicket.Id))
                .Status.Should().Be(TicketStatus.CANCELLED);
            (await verify.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "booking.booking.confirmed")).Should().Be(0);
            var refunds = await verify.OutboxEvents.AsNoTracking()
                .Where(row => row.EventType == "booking.payment_refund.requested")
                .Select(row => row.Payload)
                .ToListAsync();
            refunds.Should().HaveCount(2);
            refunds.Should().OnlyContain(payload =>
                payload.Contains("SEAT_CONFIRMATION_FAILED", StringComparison.Ordinal));
            refunds.Select(payload =>
            {
                using var document = JsonDocument.Parse(payload);
                return document.RootElement.GetProperty("bookingId").GetGuid();
            }).Should().BeEquivalentTo([first.Id, second.Id]);
            (await verify.Set<IntegrationInboxRecord>().AsNoTracking()
                .CountAsync(row => row.MessageId == paymentId)).Should().Be(1);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task ExpiryWinsRoundTripRace_AllLegsBecomeTerminalAndEmitExactRefundAllocations()
    {
        var databaseName = $"vietride_booking_payment_refund_expiry_race_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var groupId = Guid.NewGuid();
            var first = CreateBooking(groupId, 200_000);
            var second = CreateBooking(groupId, 300_000);
            first.AddPassenger("A01");
            second.AddPassenger("B01");
            await using (var setup = CreateDbContext(dataSource))
            {
                await setup.Database.MigrateAsync();
                setup.Bookings.AddRange(first, second);
                await setup.SaveChangesAsync();
            }

            await using var expiryDb = CreateDbContext(dataSource);
            await using var confirmationDb = CreateDbContext(dataSource);
            var releaseEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseExpiry = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var bookingService = new BlockingBookingService(releaseEntered, releaseExpiry);
            var expiry = new ExpireBookingOnPaymentCommandHandler(
                CreateBookingRepository(expiryDb),
                bookingService,
                new FixedClock(),
                NullLogger<ExpireBookingOnPaymentCommandHandler>.Instance,
                CreateHistoryRepository(expiryDb),
                Substitute.For<IVoucherService>());
            var paymentId = Guid.NewGuid();
            var expiryInbox = new EfIntegrationEventInbox<BookingDbContext>(
                expiryDb,
                new EfUnitOfWork(expiryDb),
                new FixedClock());
            var expiryTask = expiryInbox.ExecuteAsync(
                "booking.payment-expired",
                Guid.NewGuid(),
                new string('c', 64),
                async ct => _ = await expiry.Handle(
                    new ExpireBookingOnPaymentCommand(paymentId, "BOOKING_GROUP", groupId),
                    ct),
                CancellationToken.None);
            await releaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var trip = Substitute.For<ITripServiceClient>();
            var confirmation = new ConfirmBookingOnPaymentCommandHandler(
                CreateBookingRepository(confirmationDb),
                trip,
                new IntegrationEventOutbox(new OutboxStore(confirmationDb, new FixedClock())),
                new FixedClock(),
                NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
                CreateHistoryRepository(confirmationDb));
            var confirmationInbox = new EfIntegrationEventInbox<BookingDbContext>(
                confirmationDb,
                new EfUnitOfWork(confirmationDb),
                new FixedClock());
            var confirmationTask = confirmationInbox.ExecuteAsync(
                "booking.payment-succeeded",
                paymentId,
                new string('d', 64),
                ct => ExecuteAsync(
                    confirmation,
                    new ConfirmBookingOnPaymentCommand(
                        paymentId,
                        "BOOKING_GROUP",
                        groupId,
                        500_000,
                        "VNPAY",
                        Now.AddMinutes(-1),
                        Now.AddMinutes(1)),
                    ct),
                CancellationToken.None);

            await Task.Delay(150);
            confirmationTask.IsCompleted.Should().BeFalse();
            releaseExpiry.SetResult();
            (await expiryTask).Should().Be(IntegrationEventInboxResult.Processed);
            (await confirmationTask).Should().Be(IntegrationEventInboxResult.Processed);

            await using var verify = CreateDbContext(dataSource);
            var statuses = await verify.Bookings.AsNoTracking()
                .Where(booking => booking.BookingGroupId == groupId)
                .Select(booking => booking.Status)
                .ToListAsync();
            statuses.Should().Equal(BookingStatus.EXPIRED, BookingStatus.EXPIRED);
            var refunds = await verify.OutboxEvents.AsNoTracking()
                .Where(row => row.EventType == "booking.payment_refund.requested")
                .Select(row => row.Payload)
                .ToListAsync();
            refunds.Should().HaveCount(2);
            refunds.Sum(json =>
            {
                using var document = JsonDocument.Parse(json);
                return document.RootElement.GetProperty("amount").GetInt64();
            }).Should().Be(500_000);
            await trip.DidNotReceiveWithAnyArgs()
                .ConfirmBookedRoundTripSeatsAsync(default!, default!, default, default);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task ExpiryDuringConfirmation_WaitsThenObservesFullyConfirmedBooking()
    {
        var databaseName = $"vietride_booking_payment_refund_confirmation_race_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var booking = CreateBooking();
            booking.AddPassenger("A01");
            await using (var setup = CreateDbContext(dataSource))
            {
                await setup.Database.MigrateAsync();
                setup.Bookings.Add(booking);
                await setup.SaveChangesAsync();
            }

            await using var confirmationDb = CreateDbContext(dataSource);
            await using var expiryDb = CreateDbContext(dataSource);
            var confirmationEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseConfirmation = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var trip = Substitute.For<ITripServiceClient>();
            trip.ConfirmBookedSeatsAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    booking.Id,
                    Arg.Any<IReadOnlyList<PassengerSeatAssignment>>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => AwaitConfirmationAsync(
                    confirmationEntered,
                    releaseConfirmation));
            var paymentId = Guid.NewGuid();
            var confirmation = new ConfirmBookingOnPaymentCommandHandler(
                CreateBookingRepository(confirmationDb),
                trip,
                new IntegrationEventOutbox(new OutboxStore(confirmationDb, new FixedClock())),
                new FixedClock(),
                NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
                CreateHistoryRepository(confirmationDb));
            var confirmationInbox = new EfIntegrationEventInbox<BookingDbContext>(
                confirmationDb,
                new EfUnitOfWork(confirmationDb),
                new FixedClock());
            var confirmationTask = confirmationInbox.ExecuteAsync(
                "booking.payment-succeeded",
                paymentId,
                new string('e', 64),
                ct => ExecuteAsync(
                    confirmation,
                    new ConfirmBookingOnPaymentCommand(
                        paymentId,
                        "BOOKING",
                        booking.Id,
                        booking.TotalAmount.Amount,
                        "VNPAY",
                        Now.AddMinutes(-1),
                        Now.AddMinutes(1)),
                    ct),
                CancellationToken.None);
            await confirmationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var expiry = new ExpireBookingOnPaymentCommandHandler(
                CreateBookingRepository(expiryDb),
                Substitute.For<IBookingService>(),
                new FixedClock(),
                NullLogger<ExpireBookingOnPaymentCommandHandler>.Instance,
                CreateHistoryRepository(expiryDb),
                Substitute.For<IVoucherService>());
            var expiryInbox = new EfIntegrationEventInbox<BookingDbContext>(
                expiryDb,
                new EfUnitOfWork(expiryDb),
                new FixedClock());
            var expiryTask = expiryInbox.ExecuteAsync(
                "booking.payment-expired",
                Guid.NewGuid(),
                new string('f', 64),
                async ct => _ = await expiry.Handle(
                    new ExpireBookingOnPaymentCommand(paymentId, "BOOKING", booking.Id),
                    ct),
                CancellationToken.None);

            await Task.Delay(150);
            expiryTask.IsCompleted.Should().BeFalse();
            releaseConfirmation.SetResult();
            (await confirmationTask).Should().Be(IntegrationEventInboxResult.Processed);
            (await expiryTask).Should().Be(IntegrationEventInboxResult.Processed);

            await using var verify = CreateDbContext(dataSource);
            (await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id))
                .Status.Should().Be(BookingStatus.CONFIRMED);
            (await verify.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "booking.booking.confirmed")).Should().Be(1);
            (await verify.OutboxEvents.AsNoTracking()
                .CountAsync(row => row.EventType == "booking.payment_refund.requested")).Should().Be(0);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task FailureAfterRefundOutboxStaging_RollsBackTerminalStateOutboxAndInboxMarker()
    {
        var databaseName = $"vietride_booking_payment_refund_rollback_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            await using var db = CreateDbContext(dataSource);
            await db.Database.MigrateAsync();
            var booking = CreateBooking();
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var clock = new FixedClock();
            var unitOfWork = new EfUnitOfWork(db);
            var realOutbox = new IntegrationEventOutbox(new OutboxStore(db, clock));
            var handler = CreateHandler(db, new ThrowAfterStagingOutbox(realOutbox), clock);
            var inbox = new EfIntegrationEventInbox<BookingDbContext>(db, unitOfWork, clock);
            var paymentId = Guid.NewGuid();
            var command = new ConfirmBookingOnPaymentCommand(
                paymentId,
                "BOOKING",
                booking.Id,
                booking.TotalAmount.Amount,
                "VNPAY",
                Now,
                Now);

            var act = () => inbox.ExecuteAsync(
                "booking.payment-succeeded",
                paymentId,
                new string('b', 64),
                ct => ExecuteAsync(handler, command, ct),
                CancellationToken.None);

            await act.Should().ThrowAsync<ForcedFailureException>();
            db.ChangeTracker.Clear();
            (await db.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id))
                .Status.Should().Be(BookingStatus.PENDING_PAYMENT);
            (await db.BookingStatusHistories.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.OutboxEvents.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.Set<IntegrationInboxRecord>().AsNoTracking().CountAsync()).Should().Be(0);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static async Task ExecuteAsync(
        ConfirmBookingOnPaymentCommandHandler handler,
        ConfirmBookingOnPaymentCommand command,
        CancellationToken cancellationToken)
        => _ = await handler.Handle(command, cancellationToken);

    private static BasicDeliverEventArgs CreatePaymentSucceededDelivery(
        ulong deliveryTag,
        PaymentSucceededIntegrationEvent integrationEvent,
        bool redelivered = false)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(
            integrationEvent,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var properties = Substitute.For<IBasicProperties>();
        properties.MessageId.Returns(integrationEvent.PaymentId.ToString("D"));
        return new BasicDeliverEventArgs(
            "consumer-tag",
            deliveryTag,
            redelivered,
            "vietride.events",
            PaymentSucceededIntegrationEvent.EventType,
            properties,
            body);
    }

    private static async Task<SeatConfirmationOutcome> AwaitConfirmationAsync(
        TaskCompletionSource entered,
        TaskCompletionSource release)
    {
        entered.SetResult();
        await release.Task;
        return new SeatConfirmationOutcome.Success();
    }

    private static async Task<TripSnapshot?> AwaitTripSnapshotAsync(
        TripSnapshot snapshot,
        TaskCompletionSource entered,
        TaskCompletionSource release)
    {
        entered.SetResult();
        await release.Task;
        return snapshot;
    }

    private static async Task AssertPublisherRestartAsync(OutboxEvent row)
    {
        var unavailable = Substitute.For<IRabbitMqConnectionFactory>();
        unavailable.GetOrCreate().Returns(_ => throw new InvalidOperationException("broker unavailable"));
        var firstPublisher = CreatePublisher(unavailable);
        var firstAttempt = () => firstPublisher.PublishRawAsync(
            row.EventType,
            row.Id,
            row.Payload,
            CancellationToken.None);
        await firstAttempt.Should().ThrowAsync<InvalidOperationException>();

        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IModel>();
        var properties = Substitute.For<IBasicProperties>();
        connection.CreateModel().Returns(channel);
        channel.IsOpen.Returns(true);
        channel.CreateBasicProperties().Returns(properties);
        var restarted = Substitute.For<IRabbitMqConnectionFactory>();
        restarted.GetOrCreate().Returns(connection);
        await CreatePublisher(restarted).PublishRawAsync(
            row.EventType,
            row.Id,
            row.Payload,
            CancellationToken.None);

        properties.MessageId.Should().Be(row.Id.ToString());
        properties.Type.Should().Be("booking.payment_refund.requested");
        channel.Received(1).BasicPublish(
            "vietride.events",
            "booking.payment_refund.requested",
            false,
            properties,
            Arg.Is<ReadOnlyMemory<byte>>(body =>
                Encoding.UTF8.GetString(body.ToArray()) == row.Payload));
    }

    private static RabbitMqEventPublisher CreatePublisher(IRabbitMqConnectionFactory connections)
        => new(
            connections,
            Options.Create(new RabbitMqOptions
            {
                ExchangeName = "vietride.events",
            }),
            Substitute.For<ILogger<RabbitMqEventPublisher>>());

    private static ConfirmBookingOnPaymentCommandHandler CreateHandler(
        BookingDbContext db,
        IIntegrationEventOutbox outbox,
        IClock clock)
        => new(
            CreateBookingRepository(db),
            Substitute.For<ITripServiceClient>(),
            outbox,
            clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfirmBookingOnPaymentCommandHandler>.Instance,
            CreateHistoryRepository(db));

    private static IBookingRepository CreateBookingRepository(BookingDbContext db)
        => (IBookingRepository)Activator.CreateInstance(
            typeof(BookingDbContext).Assembly.GetType(
                "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingRepository",
                throwOnError: true)!,
            db)!;

    private static IBookingStatusHistoryRepository CreateHistoryRepository(BookingDbContext db)
        => (IBookingStatusHistoryRepository)Activator.CreateInstance(
            typeof(BookingDbContext).Assembly.GetType(
                "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingStatusHistoryRepository",
                throwOnError: true)!,
            db)!;

    private static BookingEntity CreateBooking(
        Guid? bookingGroupId = null,
        long amount = 350_000)
        => BookingEntity.CreatePendingPayment(
            BookingCode.Generate(Now),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            Money.FromRaw(amount),
            Money.Zero,
            Money.FromRaw(amount),
            bookingGroupId: bookingGroupId,
            seatLockToken: Guid.NewGuid());

    private static BookingPaymentTransitionSnapshot CreateSnapshot(BookingEntity booking)
        => new(
            booking.Id,
            booking.PassengerUserId,
            booking.TripId,
            booking.SeatLockToken,
            booking.TotalAmount.Amount,
            VoucherUsageId: null,
            [new PassengerSeatAssignment(Guid.NewGuid(), "A01")],
            ["VT-20260731-ABCDEFGH"]);

    private static TripSnapshot CreateTripSnapshot(BookingEntity booking)
        => new(
            booking.TripId,
            booking.OperatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SCHEDULED",
            Now.AddHours(24),
            Now.AddHours(28),
            booking.BaseFare.Amount,
            new TripStationSnapshot(Guid.NewGuid(), "Ha Noi"),
            new TripStationSnapshot(Guid.NewGuid(), "Da Nang"),
            [],
            new TripSeatSummary(40, 39));

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        BookingDbContext.ConfigurePostgresTypes(builder);
        builder.MapEnum<OutboxEventStatus>(
            $"{BookingDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        return builder.Build();
    }

    private static BookingDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BookingDbContext.SchemaName))
            .ConfigureWarnings(warnings =>
                warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new BookingDbContext(options, new FixedClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_BOOKING_TEST_CONNECTION_STRING");
        var template = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return new NpgsqlConnectionStringBuilder(
            template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase))
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class ThrowAfterStagingOutbox(IIntegrationEventOutbox inner)
        : IIntegrationEventOutbox
    {
        public async Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken ct = default)
        {
            await inner.EnqueueAsync(eventType, payloadJson, ct);
            throw new ForcedFailureException();
        }
    }

    private sealed class ForcedFailureException : Exception
    {
    }

    private sealed class DelegatingPaymentSucceededHandler(
        ConfirmBookingOnPaymentCommandHandler handler)
        : IIntegrationEventHandler<PaymentSucceededIntegrationEvent>
    {
        public async Task HandleAsync(
            PaymentSucceededIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
            => _ = await handler.Handle(
                new ConfirmBookingOnPaymentCommand(
                    integrationEvent.PaymentId,
                    integrationEvent.ReferenceType,
                    integrationEvent.ReferenceId,
                    integrationEvent.Amount,
                    integrationEvent.Method,
                    integrationEvent.PaidAt,
                    integrationEvent.DueAt),
                cancellationToken);
    }

    private sealed class PassThroughIntegrationEventInbox : IIntegrationEventInbox
    {
        public async Task<IntegrationEventInboxResult> ExecuteAsync(
            string consumerName,
            Guid messageId,
            string payloadHash,
            Func<CancellationToken, Task> handler,
            CancellationToken cancellationToken)
        {
            await handler(cancellationToken);
            return IntegrationEventInboxResult.Processed;
        }
    }

    private sealed class BlockingBookingService(
        TaskCompletionSource entered,
        TaskCompletionSource release) : IBookingService
    {
        private int _calls;

        public async Task ReleaseSeatsAsync(
            Guid tripId,
            Guid seatLockToken,
            IReadOnlyList<string> seatNumbers,
            CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _calls) != 1)
            {
                return;
            }

            entered.SetResult();
            await release.Task.WaitAsync(ct);
        }
    }
}
