using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;
using StackExchange.Redis;
using VietRide.Booking.Api.Controllers;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.BookingTransfers.ConfirmPassengerTransfer;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Booking.IntegrationTests.BookingTransfers;

public sealed class VehicleSubstitutionPassengerConfirmationEndpointTests
    : IClassFixture<VehicleSubstitutionPassengerConfirmationEndpointTests.ConfirmationFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 9, 0, 0, TimeSpan.Zero);

    private readonly ConfirmationFactory _factory;

    public VehicleSubstitutionPassengerConfirmationEndpointTests(ConfirmationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EscalatedTransferCanStillBeConfirmedByAssignedCrew()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var transfer = CreateTransfer(Guid.NewGuid(), tripId, "E09");
        transfer.Escalate().Should().BeTrue();
        _factory.AddActiveTransfer(transfer, tripId, operatorId);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(CreateTripSnapshot(tripId, operatorId, driverId));

        using var response = await _factory.CreateAuthenticatedClient(driverId, "DRIVER")
            .SendAsync(BuildRequest(tripId, transfer.PassengerId, Guid.NewGuid().ToString("D")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        transfer.ConfirmationStatus.Should().Be(BookingTransferConfirmationStatus.CONFIRMED);
        transfer.ConfirmedByUserId.Should().Be(driverId);
    }

    [Fact]
    public async Task AssignedCrewConfirmsExactlyThreeOfFiveWithoutChangingTwoSiblings()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var transfers = Enumerable.Range(0, 5)
            .Select(index => CreateTransfer(
                Guid.NewGuid(),
                tripId,
                $"A{index + 1:00}"))
            .ToArray();
        foreach (var transfer in transfers)
        {
            _factory.AddActiveTransfer(transfer, tripId, operatorId);
        }

        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(CreateTripSnapshot(tripId, operatorId, driverId));
        var client = _factory.CreateAuthenticatedClient(driverId, "DRIVER");

        foreach (var transfer in transfers.Take(3))
        {
            using var response = await client.SendAsync(BuildRequest(
                tripId,
                transfer.PassengerId,
                Guid.NewGuid().ToString("D")));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        transfers.Count(transfer =>
                transfer.ConfirmationStatus == BookingTransferConfirmationStatus.CONFIRMED)
            .Should().Be(3);
        transfers.Count(transfer =>
                transfer.ConfirmationStatus == BookingTransferConfirmationStatus.PENDING_CONFIRM)
            .Should().Be(2);
        transfers.Skip(3).Should().OnlyContain(transfer =>
            transfer.ConfirmedAt == null && transfer.ConfirmedByUserId == null);
        await _factory.UnitOfWork.Received(3).SaveChangesAsync(Arg.Any<CancellationToken>());
        _factory.Outbox.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task DriverAndAssistantReceiveExactResponseWithoutPassengerOrTicketMutation()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var assistantId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(tripId, operatorId);
        var passengers = booking.Passengers.ToArray();
        var tickets = booking.Tickets.ToArray();
        for (var index = 0; index < passengers.Length; index++)
        {
            passengers[index].MarkBoarded(Now.AddMinutes(-20));
            tickets[index].MarkUsed(Now.AddMinutes(-20));
            passengers[index].ApplyVehicleSubstitutionSeat($"B{index + 1:00}");
        }

        var driverTransfer = CreateTransfer(
            passengers[0].Id,
            tripId,
            "B01",
            booking.Id,
            tickets[0].Id);
        var assistantTransfer = CreateTransfer(
            passengers[1].Id,
            tripId,
            "B02",
            booking.Id,
            tickets[1].Id);
        _factory.AddActiveTransfer(driverTransfer, tripId, operatorId);
        _factory.AddActiveTransfer(assistantTransfer, tripId, operatorId);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(CreateTripSnapshot(tripId, operatorId, driverId, assistantId));

        var driverJson = await ConfirmAndReadAsync(driverId, "DRIVER", driverTransfer);
        var assistantJson = await ConfirmAndReadAsync(assistantId, "ASSISTANT", assistantTransfer);

        AssertExactSuccess(driverJson, driverTransfer, driverId);
        AssertExactSuccess(assistantJson, assistantTransfer, assistantId);
        driverTransfer.NewSeatNumber.Should().Be("B01");
        assistantTransfer.NewSeatNumber.Should().Be("B02");
        passengers.Should().OnlyContain(passenger =>
            passenger.BoardingStatus == PassengerBoardingStatus.BOARDED
            && passenger.BoardedAt == Now.AddMinutes(-20));
        tickets.Should().OnlyContain(ticket =>
            ticket.Status == TicketStatus.USED
            && ticket.UsedAt == Now.AddMinutes(-20));
        _factory.Outbox.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task CrossTripInactiveTransferWrongRoleAndUnassignedCrewDoNotMutate()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        var activeTripId = Guid.NewGuid();
        var otherTripId = Guid.NewGuid();
        var otherOperatorId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var transfer = CreateTransfer(Guid.NewGuid(), activeTripId, "C01");
        var tenantTransfer = CreateTransfer(Guid.NewGuid(), activeTripId, "C02");
        _factory.AddActiveTransfer(transfer, otherTripId, operatorId);
        _factory.AddActiveTransfer(tenantTransfer, activeTripId, operatorId);
        _factory.TripClient.GetTripSnapshotAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(call => CreateTripSnapshot(
                call.ArgAt<Guid>(0),
                call.ArgAt<Guid>(0) == activeTripId ? otherOperatorId : operatorId,
                driverId));

        var driver = _factory.CreateAuthenticatedClient(driverId, "DRIVER");
        using var crossTrip = await driver.SendAsync(BuildRequest(
            activeTripId,
            transfer.PassengerId,
            Guid.NewGuid().ToString("D")));
        await AssertErrorAsync(
            crossTrip,
            HttpStatusCode.NotFound,
            "BOOKING_TRANSFER_NOT_FOUND");

        using var inactive = await driver.SendAsync(BuildRequest(
            otherTripId,
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D")));
        await AssertErrorAsync(
            inactive,
            HttpStatusCode.NotFound,
            "BOOKING_TRANSFER_NOT_FOUND");

        using var crossTenant = await driver.SendAsync(BuildRequest(
            activeTripId,
            tenantTransfer.PassengerId,
            Guid.NewGuid().ToString("D")));
        await AssertErrorAsync(
            crossTenant,
            HttpStatusCode.NotFound,
            "BOOKING_TRANSFER_NOT_FOUND");

        var passenger = _factory.CreateAuthenticatedClient(driverId, "PASSENGER");
        using var wrongRole = await passenger.SendAsync(BuildRequest(
            activeTripId,
            transfer.PassengerId,
            Guid.NewGuid().ToString("D")));
        wrongRole.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var unassigned = _factory.CreateAuthenticatedClient(Guid.NewGuid(), "DRIVER");
        using var unassignedResponse = await unassigned.SendAsync(BuildRequest(
            activeTripId,
            transfer.PassengerId,
            Guid.NewGuid().ToString("D")));
        await AssertErrorAsync(
            unassignedResponse,
            HttpStatusCode.Forbidden,
            "FORBIDDEN");

        transfer.ConfirmationStatus.Should().Be(BookingTransferConfirmationStatus.PENDING_CONFIRM);
        tenantTransfer.ConfirmationStatus.Should()
            .Be(BookingTransferConfirmationStatus.PENDING_CONFIRM);
        transfer.ConfirmedAt.Should().BeNull();
        await _factory.UnitOfWork.DidNotReceive()
            .SaveChangesAsync(Arg.Any<CancellationToken>());
        _factory.Outbox.ReceivedCalls().Should().BeEmpty();

        await AssertPostgresBookingAndTransferLocksAsync();
    }

    [Fact]
    public async Task PendingSeatIsConflictAndAlreadyConfirmedRetriesReturnPersistedValues()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var firstActorId = Guid.NewGuid();
        var pendingSeat = CreateTransfer(Guid.NewGuid(), tripId, null);
        var confirmed = CreateTransfer(Guid.NewGuid(), tripId, "D02");
        var replayed = CreateTransfer(Guid.NewGuid(), tripId, "D03");
        var untouchedSibling = CreateTransfer(Guid.NewGuid(), tripId, "D04");
        var persistedAt = Now.AddMinutes(-5);
        confirmed.Confirm(firstActorId, persistedAt);
        _factory.AddActiveTransfer(pendingSeat, tripId, operatorId);
        _factory.AddActiveTransfer(confirmed, tripId, operatorId);
        _factory.AddActiveTransfer(replayed, tripId, operatorId);
        _factory.AddActiveTransfer(untouchedSibling, tripId, operatorId);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(CreateTripSnapshot(tripId, operatorId, driverId));
        var client = _factory.CreateAuthenticatedClient(driverId, "DRIVER");

        using var pendingResponse = await client.SendAsync(BuildRequest(
            tripId,
            pendingSeat.PassengerId,
            Guid.NewGuid().ToString("D")));
        await AssertErrorAsync(
            pendingResponse,
            HttpStatusCode.Conflict,
            "BOOKING_TRANSFER_SEAT_PENDING");

        using var retry = await client.SendAsync(BuildRequest(
            tripId,
            confirmed.PassengerId,
            Guid.NewGuid().ToString("D")));
        var retryJson = await retry.Content.ReadAsStringAsync();
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertExactSuccess(retryJson, confirmed, firstActorId);
        confirmed.ConfirmedAt.Should().Be(persistedAt);
        confirmed.ConfirmedByUserId.Should().Be(firstActorId);

        var replayKey = Guid.NewGuid().ToString("D");
        using var firstRequest = BuildRequest(tripId, replayed.PassengerId, replayKey);
        using var replayRequest = BuildRequest(tripId, replayed.PassengerId, replayKey);
        using var firstResponse = await client.SendAsync(firstRequest);
        var firstJson = await firstResponse.Content.ReadAsStringAsync();
        using var replayResponse = await client.SendAsync(replayRequest);
        var replayJson = await replayResponse.Content.ReadAsStringAsync();
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        replayJson.Should().Be(firstJson);
        replayed.ConfirmedAt.Should().Be(Now);
        replayed.ConfirmedByUserId.Should().Be(driverId);
        untouchedSibling.ConfirmationStatus.Should()
            .Be(BookingTransferConfirmationStatus.PENDING_CONFIRM);
        untouchedSibling.ConfirmedAt.Should().BeNull();
        await _factory.UnitOfWork.Received(1)
            .SaveChangesAsync(Arg.Any<CancellationToken>());
        await _factory.TransferRepository.Received(3).GetActiveForConfirmationAsync(
            Arg.Any<Guid>(),
            tripId,
            operatorId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BodylessStrictRouteAndUuidV4IdempotencyAreEnforced()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var transfer = CreateTransfer(Guid.NewGuid(), tripId, "E01");
        _factory.AddActiveTransfer(transfer, tripId, operatorId);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(CreateTripSnapshot(tripId, operatorId, driverId));
        var client = _factory.CreateAuthenticatedClient(driverId, "DRIVER");

        using var missingKey = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/bookings/trips/{tripId:D}/transfers/passengers/{transfer.PassengerId:D}/confirm");
        using var missingKeyResponse = await client.SendAsync(missingKey);
        await AssertErrorAsync(
            missingKeyResponse,
            HttpStatusCode.UnprocessableEntity,
            "IDEMPOTENCY_KEY_REQUIRED");

        using var invalidRoute = await client.SendAsync(BuildRequest(
            "not-a-guid",
            transfer.PassengerId.ToString("D"),
            Guid.NewGuid().ToString("D")));
        await AssertErrorAsync(invalidRoute, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");

        using var invalidKey = await client.SendAsync(BuildRequest(
            tripId.ToString("D"),
            transfer.PassengerId.ToString("D"),
            Guid.NewGuid().ToString("N")));
        await AssertErrorAsync(invalidKey, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");

        using var bodyRequest = BuildRequest(
            tripId,
            transfer.PassengerId,
            Guid.NewGuid().ToString("D"));
        bodyRequest.Content = new ChunkedJsonContent("{}");
        using var bodyResponse = await client.SendAsync(bodyRequest);
        await AssertErrorAsync(bodyResponse, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");

        using var extraSegment = await client.SendAsync(BuildRequest(
            $"{tripId:D}/extra",
            transfer.PassengerId.ToString("D"),
            Guid.NewGuid().ToString("D")));
        extraSegment.StatusCode.Should().Be(HttpStatusCode.NotFound);
        transfer.ConfirmationStatus.Should().Be(BookingTransferConfirmationStatus.PENDING_CONFIRM);
    }

    [Fact]
    public void ThinControllerDispatchesMediatRAndDeclaresApiResponseAndSwashbuckleMetadata()
    {
        var controllerType = typeof(BookingTransfersController);
        controllerType.GetConstructors().Should().ContainSingle()
            .Which.GetParameters().Select(parameter => parameter.ParameterType)
            .Should().Equal(typeof(ISender));
        controllerType.GetCustomAttribute<AuthorizeAttribute>()!.Roles
            .Should().Be("DRIVER,ASSISTANT");

        var action = controllerType.GetMethod(
            nameof(BookingTransfersController.ConfirmPassengerTransfer))!;
        action.GetParameters().Should().NotContain(parameter =>
            parameter.GetCustomAttribute<FromBodyAttribute>() != null);
        var idempotency = action.GetCustomAttribute<RequireIdempotencyAttribute>();
        idempotency.Should().NotBeNull();
        idempotency!.AllowRequestBody.Should().BeFalse();
        var statuses = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Order()
            .ToArray();
        statuses.Should().Equal(200, 401, 403, 404, 409, 422);
        action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == 200)
            .Type.Should().Be(typeof(ApiResponse<ConfirmPassengerTransferResponse>));
        typeof(ConfirmPassengerTransferCommand).GetInterfaces()
            .Should().Contain(typeof(IRequest<ConfirmPassengerTransferResponse>));
    }

    private async Task<string> ConfirmAndReadAsync(
        Guid callerId,
        string role,
        BookingTransfer transfer)
    {
        var client = _factory.CreateAuthenticatedClient(callerId, role);
        using var response = await client.SendAsync(BuildRequest(
            transfer.NewTripId,
            transfer.PassengerId,
            Guid.NewGuid().ToString("D")));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static void AssertExactSuccess(
        string json,
        BookingTransfer transfer,
        Guid confirmedByUserId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("statusCode").GetInt32().Should().Be(200);
        root.TryGetProperty("error", out _).Should().BeFalse();
        var data = root.GetProperty("data");
        data.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "bookingTransferId",
            "passengerId",
            "newTripId",
            "confirmationStatus",
            "confirmedAt",
            "confirmedByUserId");
        data.GetProperty("bookingTransferId").GetGuid().Should().Be(transfer.Id);
        data.GetProperty("passengerId").GetGuid().Should().Be(transfer.PassengerId);
        data.GetProperty("newTripId").GetGuid().Should().Be(transfer.NewTripId);
        data.GetProperty("confirmationStatus").GetString().Should().Be("CONFIRMED");
        data.GetProperty("confirmedAt").GetDateTimeOffset().Should().Be(transfer.ConfirmedAt);
        data.GetProperty("confirmedByUserId").GetGuid().Should().Be(confirmedByUserId);
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        response.StatusCode.Should().Be(status);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("statusCode").GetInt32().Should().Be((int)status);
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(code);
    }

    private static HttpRequestMessage BuildRequest(
        Guid tripId,
        Guid passengerId,
        string idempotencyKey)
        => BuildRequest(tripId.ToString("D"), passengerId.ToString("D"), idempotencyKey);

    private static HttpRequestMessage BuildRequest(
        string tripId,
        string passengerId,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/bookings/trips/{tripId}/transfers/passengers/{passengerId}/confirm");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static BookingTransfer CreateTransfer(
        Guid passengerId,
        Guid newTripId,
        string? newSeatNumber,
        Guid? bookingId = null,
        Guid? ticketId = null)
        => BookingTransfer.Create(
            bookingId ?? Guid.NewGuid(),
            passengerId,
            ticketId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            newTripId,
            "OLD-01",
            newSeatNumber,
            BookingTransferConfirmationStatus.PENDING_CONFIRM,
            Now.AddMinutes(-10),
            Guid.NewGuid());

    private static VietRide.Booking.Domain.Entities.Booking CreateConfirmedBooking(
        Guid tripId,
        Guid operatorId)
    {
        var booking = VietRide.Booking.Domain.Entities.Booking.CreatePendingPayment(
            BookingCode.Generate(Now),
            Guid.NewGuid(),
            tripId,
            operatorId,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            Money.FromRaw(400_000),
            Money.Zero,
            Money.FromRaw(400_000));
        booking.AddTicketedPassenger(
            "OLD-01",
            TicketCode.Generate(Now),
            Money.FromRaw(200_000),
            Money.Zero,
            Money.FromRaw(200_000));
        booking.AddTicketedPassenger(
            "OLD-02",
            TicketCode.Generate(Now.AddSeconds(1)),
            Money.FromRaw(200_000),
            Money.Zero,
            Money.FromRaw(200_000));
        booking.Confirm(Now.AddMinutes(-30));
        return booking;
    }

    private static TripSnapshot CreateTripSnapshot(
        Guid tripId,
        Guid operatorId,
        Guid driverUserId,
        Guid? assistantUserId = null)
        => new(
            tripId,
            operatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "BOARDING",
            Now.AddHours(1),
            Now.AddHours(5),
            200_000,
            new TripStationSnapshot(Guid.NewGuid(), "Origin"),
            new TripStationSnapshot(Guid.NewGuid(), "Destination"),
            [],
            new TripSeatSummary(40, 35),
            DriverUserId: driverUserId,
            AssistantUserId: assistantUserId);

    private static async Task AssertPostgresBookingAndTransferLocksAsync()
    {
        var databaseName = $"vietride_booking_transfer_confirm_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            await using var setup = CreateDbContext(dataSource);
            await setup.Database.MigrateAsync();

            var operatorId = Guid.NewGuid();
            var tripId = Guid.NewGuid();
            var nextTripId = Guid.NewGuid();
            var booking = CreateConfirmedBooking(tripId, operatorId);
            var passenger = booking.Passengers.First();
            var ticket = booking.Tickets.First();
            passenger.MarkBoarded(Now.AddMinutes(-20));
            ticket.MarkUsed(Now.AddMinutes(-20));
            passenger.ApplyVehicleSubstitutionSeat("LOCK-01");
            var transfer = CreateTransfer(
                passenger.Id,
                tripId,
                "LOCK-01",
                booking.Id,
                ticket.Id);
            transfer.Escalate().Should().BeTrue();

            setup.Bookings.Add(booking);
            setup.BookingTransfers.Add(transfer);
            await setup.SaveChangesAsync();

            await using var lockingDb = CreateDbContext(dataSource);
            var repository = CreateBookingTransferRepository(lockingDb);
            (await repository.GetActiveForConfirmationAsync(
                    passenger.Id,
                    tripId,
                    Guid.NewGuid()))
                .Should().BeNull("the repository must enforce the Booking tenant");

            await using var lockingTransaction =
                await lockingDb.Database.BeginTransactionAsync();
            var lockedTransfer = await repository.GetActiveForConfirmationAsync(
                passenger.Id,
                tripId,
                operatorId);
            lockedTransfer.Should().NotBeNull();

            var updateStarted =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var competingUpdate = Task.Run(async () =>
            {
                await using var competingDb = CreateDbContext(dataSource);
                await using var transaction =
                    await competingDb.Database.BeginTransactionAsync();
                updateStarted.SetResult();
                await competingDb.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE vietride_booking.bookings
                    SET trip_id = {nextTripId}
                    WHERE id = {booking.Id}
                    """);
                await transaction.CommitAsync();
            });

            await updateStarted.Task;
            await Task.Delay(200);
            competingUpdate.IsCompleted.Should().BeFalse(
                "the confirmation query must lock the joined Booking row");

            lockedTransfer!.Confirm(Guid.NewGuid(), Now);
            await lockingDb.SaveChangesAsync();
            await lockingTransaction.CommitAsync();
            await competingUpdate.WaitAsync(TimeSpan.FromSeconds(5));

            await using var verify = CreateDbContext(dataSource);
            var persistedTransfer = await verify.BookingTransfers
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == transfer.Id);
            persistedTransfer.ConfirmationStatus.Should()
                .Be(BookingTransferConfirmationStatus.CONFIRMED);
            persistedTransfer.ConfirmedAt.Should().Be(Now);
            (await verify.Bookings.AsNoTracking()
                    .SingleAsync(candidate => candidate.Id == booking.Id))
                .TripId.Should().Be(nextTripId);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static IBookingTransferRepository CreateBookingTransferRepository(
        BookingDbContext db)
        => (IBookingTransferRepository)Activator.CreateInstance(
            typeof(BookingDbContext).Assembly.GetType(
                "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingTransferRepository",
                throwOnError: true)!,
            db)!;

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        BookingDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    private static BookingDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    BookingDbContext.SchemaName))
            .ConfigureWarnings(warnings =>
                warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new BookingDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable(
            "VIETRIDE_BOOKING_TEST_CONNECTION_STRING");
        var template = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        var connectionString = template.Replace(
            "{databaseName}",
            databaseName,
            StringComparison.OrdinalIgnoreCase);
        return new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(
        string connectionString,
        string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"CREATE DATABASE \"{databaseName}\";",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(
        string connectionString,
        string databaseName)
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

    private sealed class ChunkedJsonContent : HttpContent
    {
        private readonly byte[] _bytes;

        public ChunkedJsonContent(string json)
        {
            _bytes = Encoding.UTF8.GetBytes(json);
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
            => stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

    }

    public sealed class ConfirmationFactory : WebApplicationFactory<Program>
    {
        private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";
        private readonly List<BookingTransfer> _transfers = [];
        private readonly Dictionary<Guid, Guid> _currentTripsByBooking = [];
        private readonly Dictionary<Guid, Guid> _operatorsByBooking = [];

        public ITripServiceClient TripClient { get; } = Substitute.For<ITripServiceClient>();
        public IBookingTransferRepository TransferRepository { get; }
            = Substitute.For<IBookingTransferRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IIntegrationEventOutbox Outbox { get; } = Substitute.For<IIntegrationEventOutbox>();

        public void AddActiveTransfer(
            BookingTransfer transfer,
            Guid currentTripId,
            Guid operatorId)
        {
            _transfers.Add(transfer);
            _currentTripsByBooking[transfer.BookingId] = currentTripId;
            _operatorsByBooking[transfer.BookingId] = operatorId;
        }

        public void Reset()
        {
            _transfers.Clear();
            _currentTripsByBooking.Clear();
            _operatorsByBooking.Clear();
            TripClient.ClearReceivedCalls();
            TransferRepository.ClearReceivedCalls();
            UnitOfWork.ClearReceivedCalls();
            Outbox.ClearReceivedCalls();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting(
                "ConnectionStrings:Default",
                "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
            builder.UseEnvironment("Testing");

            builder.ConfigureTestServices(services =>
            {
                TransferRepository.GetActiveForConfirmationAsync(
                        Arg.Any<Guid>(),
                        Arg.Any<Guid>(),
                        Arg.Any<Guid>(),
                        Arg.Any<CancellationToken>())
                    .Returns(call =>
                    {
                        var passengerId = call.ArgAt<Guid>(0);
                        var newTripId = call.ArgAt<Guid>(1);
                        var operatorId = call.ArgAt<Guid>(2);
                        return _transfers
                            .Where(transfer =>
                                transfer.PassengerId == passengerId
                                && transfer.NewTripId == newTripId
                                && _currentTripsByBooking.GetValueOrDefault(transfer.BookingId)
                                    == newTripId
                                && _operatorsByBooking.GetValueOrDefault(transfer.BookingId)
                                    == operatorId
                                && transfer.ConfirmationStatus
                                    is BookingTransferConfirmationStatus.PENDING_CONFIRM
                                        or BookingTransferConfirmationStatus.ESCALATED
                                        or BookingTransferConfirmationStatus.CONFIRMED)
                            .OrderByDescending(transfer => transfer.TransferredAt)
                            .FirstOrDefault();
                    });
                UnitOfWork.ExecuteInTransactionAsync(
                        Arg.Any<Func<Task<ConfirmPassengerTransferResponse>>>(),
                        Arg.Any<CancellationToken>())
                    .Returns(call =>
                        call.Arg<Func<Task<ConfirmPassengerTransferResponse>>>()());
                UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

                services.AddSingleton(TripClient);
                services.AddSingleton(TransferRepository);
                services.AddSingleton(UnitOfWork);
                services.AddSingleton(Outbox);

                var clock = Substitute.For<IClock>();
                clock.UtcNow.Returns(Now);
                services.AddSingleton(clock);
                services.AddSingleton<IConnectionMultiplexer>(InMemoryIdempotencyRedis.Create());
            });
        }

        public HttpClient CreateAuthenticatedClient(Guid userId, string role)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add(
                "X-Internal-Auth",
                $"Bearer {MintInternalJwt(userId, role)}");
            return client;
        }

        private static string MintInternalJwt(Guid userId, string role)
        {
            var now = DateTimeOffset.UtcNow;
            var header = Base64UrlEncode(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["alg"] = "HS256",
                    ["typ"] = "JWT",
                })));
            var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["iss"] = "vietride-gateway",
                    ["aud"] = "vietride-internal",
                    ["sub"] = userId.ToString("D"),
                    ["role"] = role,
                    ["jti"] = Guid.NewGuid().ToString("N"),
                    ["iat"] = now.ToUnixTimeSeconds(),
                    ["nbf"] = now.ToUnixTimeSeconds(),
                    ["exp"] = now.AddSeconds(120).ToUnixTimeSeconds(),
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            var signingInput = $"{header}.{payload}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
            var signature = Base64UrlEncode(
                hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
            return $"{signingInput}.{signature}";
        }

        private static string Base64UrlEncode(byte[] bytes)
            => Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
    }
}
