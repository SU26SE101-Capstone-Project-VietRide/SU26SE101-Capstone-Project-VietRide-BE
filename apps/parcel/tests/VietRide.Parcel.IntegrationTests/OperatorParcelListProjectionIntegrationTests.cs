using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.OperatorList;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.IntegrationTests;

public sealed class OperatorParcelListProjectionIntegrationTests
{
    [Fact]
    public async Task Handler_UsesTenantPageAndReturnsOldAndNewContractFromOneBatchPerUpstream()
    {
        var databaseName = $"vietride_parcel_ui12_list_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var operatorId = Guid.NewGuid();
            var otherOperatorId = Guid.NewGuid();
            var owned = CreateParcel(operatorId, "VRP-UI12-OWNED");
            var other = CreateParcel(otherOperatorId, "VRP-UI12-OTHER");
            var snapshotRouteId = Guid.NewGuid();
            var snapshotVehicleId = Guid.NewGuid();
            owned.CaptureTripDisplaySnapshot(
                snapshotRouteId,
                "Snapshot Route",
                "Snapshot Origin",
                "Snapshot Destination",
                snapshotVehicleId,
                "51S-12121");

            await using (var seed = CreateDbContext(dataSource))
            {
                await seed.Database.MigrateAsync();
                seed.Parcels.AddRange(owned, other);
                await seed.SaveChangesAsync();
            }

            var (tripClient, tripProxy) = TripClientProxy.Create();
            var (identityClient, identityProxy) = IdentityClientProxy.Create();
            await using var context = CreateDbContext(dataSource);
            var handler = new GetOperatorParcelsQueryHandler(
                CreateRepository(context),
                tripClient,
                identityClient);

            var result = await handler.Handle(
                new GetOperatorParcelsQuery(operatorId, null, null, null, 1, 20),
                CancellationToken.None);

            var item = result.Items.Should().ContainSingle().Which;
            item.ParcelId.Should().Be(owned.Id);
            item.ParcelCode.Should().Be(owned.ParcelCode);
            item.TripId.Should().Be(owned.TripId);
            item.SenderUserId.Should().Be(owned.SenderUserId);
            item.Route.Should().Be(new OperatorParcelRouteResponse(
                snapshotRouteId,
                "Snapshot Route",
                "Snapshot Origin",
                "Snapshot Destination"));
            item.Trip.Vehicle.Should().Be(new OperatorParcelVehicleResponse(
                snapshotVehicleId,
                "51S-12121"));
            item.Trip.Status.Should().Be("SCHEDULED");
            item.Sender.DisplayName.Should().Be("Integrated Sender");
            item.Recipient.DisplayName.Should().Be(owned.RecipientName);
            tripProxy.Calls.Should().Be(1);
            identityProxy.Calls.Should().Be(1);
            tripProxy.RequestedIds.Should().Equal(owned.TripId);
            identityProxy.RequestedIds.Should().Equal(owned.SenderUserId);

            using var json = JsonDocument.Parse(JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            json.RootElement.TryGetProperty("tripId", out _).Should().BeTrue();
            json.RootElement.TryGetProperty("senderUserId", out _).Should().BeTrue();
            json.RootElement.GetProperty("trip").GetProperty("vehicle").GetProperty("licensePlate")
                .GetString().Should().Be("51S-12121");
            json.RootElement.GetProperty("route").GetProperty("routeName")
                .GetString().Should().Be("Snapshot Route");
            json.RootElement.GetProperty("sender").GetProperty("displayName")
                .GetString().Should().Be("Integrated Sender");
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static ParcelEntity CreateParcel(Guid operatorId, string parcelCode)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            parcelCode,
            Guid.NewGuid(),
            null,
            "Persisted Recipient",
            PhoneNumber.Normalize("+84912345678"),
            null,
            operatorId,
            Guid.NewGuid(),
            null,
            null,
            "Fragile",
            null,
            ParcelSizeCategory.MEDIUM,
            5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));
        return parcel;
    }

    private static IParcelRepository CreateRepository(ParcelDbContext dbContext)
    {
        var repositoryType = typeof(ParcelDbContext).Assembly.GetType(
            "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelRepository",
            throwOnError: true)!;
        return (IParcelRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        ParcelDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    private static ParcelDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = ParcelIntegrationDbContextOptions.Create(dataSource);
        return new ParcelDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_PARCEL_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return new NpgsqlConnectionStringBuilder(
            connectionString.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase))
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private class TripClientProxy : DispatchProxy
    {
        public int Calls { get; private set; }
        public IReadOnlyList<Guid> RequestedIds { get; private set; } = [];

        public static (ITripServiceClient Client, TripClientProxy Proxy) Create()
        {
            var client = DispatchProxy.Create<ITripServiceClient, TripClientProxy>();
            return (client, (TripClientProxy)(object)client);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(ITripServiceClient.GetTripSummariesAsync))
                throw new NotSupportedException(targetMethod?.Name);

            Calls++;
            RequestedIds = ((IReadOnlyCollection<Guid>)args![0]!).ToArray();
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(TripSummaryBatchOutcome.Success(
                RequestedIds.Select(tripId => new TripSummarySnapshot(
                    tripId,
                    "SCHEDULED",
                    now.AddHours(1),
                    now.AddHours(9),
                    new TripRouteSummarySnapshot(Guid.NewGuid(), "Current Route", "Current Origin", "Current Destination"),
                    new TripVehicleSummarySnapshot(Guid.NewGuid(), "51C-34343", "ACTIVE"))).ToArray()));
        }
    }

    private class IdentityClientProxy : DispatchProxy
    {
        public int Calls { get; private set; }
        public IReadOnlyList<Guid> RequestedIds { get; private set; } = [];

        public static (IIdentityServiceClient Client, IdentityClientProxy Proxy) Create()
        {
            var client = DispatchProxy.Create<IIdentityServiceClient, IdentityClientProxy>();
            return (client, (IdentityClientProxy)(object)client);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IIdentityServiceClient.GetUsersAsync))
                throw new NotSupportedException(targetMethod?.Name);

            Calls++;
            RequestedIds = ((IReadOnlyCollection<Guid>)args![0]!).ToArray();
            return Task.FromResult(IdentityUserBatchOutcome.Success(
                RequestedIds.Select(userId => new IdentityUserSummary(
                    userId,
                    "Integrated Sender",
                    "+84901112223",
                    "sender@example.test",
                    null,
                    "PASSENGER",
                    null,
                    "ACTIVE",
                    false)).ToArray()));
        }
    }
}
