using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.UnitTests.Domain;

public sealed class VehicleEntityAndModelTests
{
    [Fact]
    public void Vehicle_Create_PreservesOpaqueSeatLayoutAndActivationContracts()
    {
        using var document = JsonDocument.Parse("""{"version":1,"totalSeats":45,"seats":[]}""");

        var vehicle = Vehicle.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            " 51A-123.45 ",
            document.RootElement,
            totalSeats: 45,
            maxCargoWeightKg: 1_250.50m,
            maxCargoVolumeM3: null);

        vehicle.LicensePlate.Should().Be("51A-123.45");
        vehicle.SeatLayoutJson.GetProperty("version").GetInt32().Should().Be(1);
        vehicle.Status.Should().Be(VehicleStatus.ACTIVE);
        vehicle.IsActive.Should().BeTrue();
        vehicle.DeletedAt.Should().BeNull();
        vehicle.Should().BeAssignableTo<ISoftDeletable>();
        vehicle.Should().BeAssignableTo<IActivatable>();
    }

    [Fact]
    public void Vehicle_Create_RejectsNonPositiveSeatsAndNegativeCargoWeight()
    {
        using var document = JsonDocument.Parse("""{"version":1}""");

        var invalidSeats = () => Vehicle.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "51A-123.45",
            document.RootElement,
            totalSeats: 0,
            maxCargoWeightKg: null,
            maxCargoVolumeM3: null);

        var invalidWeight = () => Vehicle.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "51A-123.45",
            document.RootElement,
            totalSeats: 45,
            maxCargoWeightKg: -0.01m,
            maxCargoVolumeM3: null);

        invalidSeats.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("totalSeats");
        invalidWeight.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("maxCargoWeightKg");
    }

    [Fact]
    public void DriverSchedule_Create_RejectsInvalidDateRange()
    {
        using var document = JsonDocument.Parse("[1,3,5]");

        var act = () => DriverSchedule.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            vehicleId: null,
            Guid.NewGuid(),
            assistantUserId: null,
            document.RootElement,
            new TimeOnly(8, 30),
            new DateOnly(2026, 7, 2),
            new DateOnly(2026, 7, 1));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("validUntil");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[0]")]
    [InlineData("[8]")]
    [InlineData("[\"1\"]")]
    [InlineData("[1.5]")]
    public void DriverSchedule_Create_RejectsInvalidDayOfWeek(string dayOfWeekJson)
    {
        using var document = JsonDocument.Parse(dayOfWeekJson);

        var act = () => DriverSchedule.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            vehicleId: null,
            Guid.NewGuid(),
            assistantUserId: null,
            document.RootElement,
            new TimeOnly(8, 30),
            new DateOnly(2026, 7, 1),
            validUntil: null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TripModel_MapsDay9VehicleTablesExactly()
    {
        using var dbContext = CreateDbContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        var vehicleType = model.FindEntityType(typeof(VehicleType));
        vehicleType.Should().NotBeNull();
        var vehicleTypeEntity = vehicleType!;
        vehicleTypeEntity.GetTableName().Should().Be("vehicle_types");
        vehicleTypeEntity.FindProperty(nameof(VehicleType.Code))!.GetMaxLength().Should().Be(50);
        vehicleTypeEntity.FindProperty(nameof(VehicleType.EstimatedPassengerLuggageKgPerSeat))!.IsNullable.Should().BeTrue();
        vehicleTypeEntity.FindProperty(nameof(VehicleType.DefaultSeatCount))!.IsNullable.Should().BeTrue();
        vehicleTypeEntity.FindProperty("DeletedAt").Should().BeNull();
        vehicleTypeEntity.GetIndexes().Select(index => index.GetDatabaseName()).Should().BeEquivalentTo(new[]
        {
            "uq_vehicle_types_code",
            "idx_vehicle_types_is_active",
        });

        var vehicle = model.FindEntityType(typeof(Vehicle));
        vehicle.Should().NotBeNull();
        var vehicleEntity = vehicle!;
        vehicleEntity.GetTableName().Should().Be("vehicles");
        vehicleEntity.FindProperty(nameof(Vehicle.SeatLayoutJson))!.GetColumnType().Should().Be("jsonb");
        vehicleEntity.FindProperty(nameof(Vehicle.Status))!.GetColumnType().Should().Be("vehicle_status");
        vehicleEntity.FindProperty(nameof(Vehicle.MaxCargoWeightKg))!.GetColumnType().Should().Be("numeric(8,2)");
        vehicleEntity.FindProperty(nameof(Vehicle.MaxCargoVolumeM3))!.GetColumnType().Should().Be("numeric(8,2)");
        vehicleEntity.FindProperty(nameof(Vehicle.DeletedAt)).Should().NotBeNull();
        vehicleEntity.GetIndexes().Select(index => index.GetDatabaseName()).Should().BeEquivalentTo(new[]
        {
            "uq_vehicles_license_plate",
            "idx_vehicles_operator_status",
            "idx_vehicles_vehicle_type_id",
        });
        vehicleEntity.GetIndexes().Single(index => index.GetDatabaseName() == "uq_vehicles_license_plate")
            .GetFilter().Should().Be("deleted_at IS NULL");
        vehicleEntity.GetCheckConstraints().Select(check => check.Name).Should().Contain(new[]
        {
            "chk_vehicles_total_seats_positive",
            "chk_vehicles_cargo_weight_non_negative",
        });

        var vehicleTypeForeignKey = vehicleEntity.GetForeignKeys().Single();
        vehicleTypeForeignKey.PrincipalEntityType.ClrType.Should().Be(typeof(VehicleType));
        vehicleTypeForeignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);

        var vehicleQueryFilter = vehicleEntity.GetQueryFilter();
        vehicleQueryFilter.Should().NotBeNull();
        using var seatLayout = JsonDocument.Parse("""{"version":1}""");
        var activeVehicle = Vehicle.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "51A-123.45",
            seatLayout.RootElement,
            totalSeats: 1,
            maxCargoWeightKg: null,
            maxCargoVolumeM3: null);
        var deletedVehicle = Vehicle.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "51A-543.21",
            seatLayout.RootElement,
            totalSeats: 1,
            maxCargoWeightKg: null,
            maxCargoVolumeM3: null);
        deletedVehicle.SoftDelete(new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero));

        var compiledVehicleQueryFilter = vehicleQueryFilter!.Compile();
        compiledVehicleQueryFilter.DynamicInvoke(activeVehicle).Should().Be(true);
        compiledVehicleQueryFilter.DynamicInvoke(deletedVehicle).Should().Be(false);

        var schedule = model.FindEntityType(typeof(DriverSchedule));
        schedule.Should().NotBeNull();
        var scheduleEntity = schedule!;
        scheduleEntity.GetTableName().Should().Be("driver_schedules");
        scheduleEntity.FindProperty(nameof(DriverSchedule.DayOfWeek))!.GetColumnType().Should().Be("jsonb");
        scheduleEntity.FindProperty(nameof(DriverSchedule.DepartureTime))!.GetColumnType().Should().Be("time without time zone");
        scheduleEntity.FindProperty(nameof(DriverSchedule.ValidFrom))!.GetColumnType().Should().Be("date");
        scheduleEntity.FindProperty("DeletedAt").Should().BeNull();
        scheduleEntity.GetIndexes().Select(index => index.GetDatabaseName()).Should().BeEquivalentTo(new[]
        {
            "idx_driver_schedules_operator_active",
            "idx_driver_schedules_driver_active",
            "idx_driver_schedules_vehicle_active",
            "idx_driver_schedules_route_active",
        });
        scheduleEntity.GetCheckConstraints().Select(check => check.Name)
            .Should().Contain("chk_driver_schedules_valid_until_after_from");
        scheduleEntity.GetForeignKeys().Should().Contain(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Route)
            && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        scheduleEntity.GetForeignKeys().Should().Contain(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Vehicle)
            && foreignKey.DeleteBehavior == DeleteBehavior.SetNull);
        scheduleEntity.GetForeignKeys().Should().NotContain(foreignKey =>
            foreignKey.Properties.Any(property =>
                property.Name == nameof(DriverSchedule.OperatorId)
                || property.Name == nameof(DriverSchedule.DriverUserId)
                || property.Name == nameof(DriverSchedule.AssistantUserId)));
    }

    private static TripDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql("Host=localhost;Database=vietride_trip_unit;Username=postgres;Password=postgres")
            .Options;

        return new TripDbContext(options, new FrozenClock());
    }

    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 6, 11, 0, 0, 0, TimeSpan.Zero);
    }
}
