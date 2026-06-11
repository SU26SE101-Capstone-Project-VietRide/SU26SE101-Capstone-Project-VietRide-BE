using Microsoft.EntityFrameworkCore;
using Npgsql;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class VehicleRepository : IVehicleRepository
{
    private const string VehicleSelectSql = """
        SELECT
            id,
            operator_id,
            vehicle_type_id,
            license_plate,
            seat_layout_json,
            total_seats,
            max_cargo_weight_kg,
            max_cargo_volume_m3,
            status::text AS status,
            is_active,
            deleted_at,
            created_at,
            updated_at
        FROM vietride_trip.vehicles
        """;

    private readonly TripDbContext dbContext;

    public VehicleRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken ct)
        => Query().FirstOrDefaultAsync(vehicle => vehicle.Id == id, ct);

    public Task<Vehicle> AddAsync(Vehicle entity, CancellationToken ct)
    {
        dbContext.Vehicles.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(Vehicle entity)
        => dbContext.Vehicles.Update(entity);

    public void Remove(Vehicle entity)
        => dbContext.Vehicles.Remove(entity);

    public IQueryable<Vehicle> Query()
        => dbContext.Vehicles.FromSqlRaw(VehicleSelectSql);

    public IQueryable<Vehicle> QueryNoTracking()
        => dbContext.Vehicles.FromSqlRaw(VehicleSelectSql).AsNoTracking();

    public Task<Vehicle?> GetOwnedByIdAsync(
        Guid operatorId,
        Guid vehicleId,
        CancellationToken cancellationToken)
        => Query().FirstOrDefaultAsync(vehicle =>
            vehicle.Id == vehicleId
            && vehicle.OperatorId == operatorId
            && vehicle.DeletedAt == null,
            cancellationToken);

    public async Task<PagedResult<Vehicle>> ListByOperatorAsync(
        Guid operatorId,
        int page,
        int pageSize,
        string? search,
        string? searchIn,
        string? sortBy,
        string sortDir,
        CancellationToken cancellationToken)
    {
        var query = QueryNoTracking()
            .Where(vehicle => vehicle.OperatorId == operatorId && vehicle.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(vehicle => EF.Functions.ILike(vehicle.LicensePlate, pattern));
        }

        var totalItems = await query.LongCountAsync(cancellationToken);
        var items = await ApplySort(query, sortBy, sortDir)
            .ThenBy(vehicle => vehicle.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<Vehicle>.Create(items, page, pageSize, totalItems);
    }

    public Task<bool> LicensePlateExistsAsync(
        string licensePlate,
        Guid? excludedVehicleId,
        CancellationToken cancellationToken)
        => dbContext.Vehicles.AnyAsync(vehicle =>
            vehicle.LicensePlate == licensePlate
            && vehicle.DeletedAt == null
            && (!excludedVehicleId.HasValue || vehicle.Id != excludedVehicleId.Value),
            cancellationToken);

    public async Task<bool> TryAddAsync(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        vehicle.CreatedAt = now;
        vehicle.UpdatedAt = now;

        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO vietride_trip.vehicles (
                    id,
                    operator_id,
                    vehicle_type_id,
                    license_plate,
                    seat_layout_json,
                    total_seats,
                    max_cargo_weight_kg,
                    max_cargo_volume_m3,
                    status,
                    is_active,
                    deleted_at,
                    created_at,
                    updated_at)
                VALUES (
                    {vehicle.Id},
                    {vehicle.OperatorId},
                    {vehicle.VehicleTypeId},
                    {vehicle.LicensePlate},
                    CAST({vehicle.SeatLayoutJson.GetRawText()} AS jsonb),
                    {vehicle.TotalSeats},
                    {vehicle.MaxCargoWeightKg},
                    {vehicle.MaxCargoVolumeM3},
                    CAST({vehicle.Status.ToString()} AS vehicle_status),
                    {vehicle.IsActive},
                    {vehicle.DeletedAt},
                    {vehicle.CreatedAt},
                    {vehicle.UpdatedAt})
                """, cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (IsLicensePlateUniqueViolation(exception))
        {
            return false;
        }
    }

    public async Task<bool> TryUpdateAsync(Vehicle vehicle, CancellationToken cancellationToken)
    {
        vehicle.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_trip.vehicles
                SET
                    vehicle_type_id = {vehicle.VehicleTypeId},
                    license_plate = {vehicle.LicensePlate},
                    seat_layout_json = CAST({vehicle.SeatLayoutJson.GetRawText()} AS jsonb),
                    total_seats = {vehicle.TotalSeats},
                    max_cargo_weight_kg = {vehicle.MaxCargoWeightKg},
                    max_cargo_volume_m3 = {vehicle.MaxCargoVolumeM3},
                    status = CAST({vehicle.Status.ToString()} AS vehicle_status),
                    is_active = {vehicle.IsActive},
                    updated_at = {vehicle.UpdatedAt}
                WHERE id = {vehicle.Id}
                    AND deleted_at IS NULL
                """, cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (IsLicensePlateUniqueViolation(exception))
        {
            return false;
        }
    }

    private static bool IsLicensePlateUniqueViolation(PostgresException exception)
        => exception is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "uq_vehicles_license_plate",
        };

    private static IOrderedQueryable<Vehicle> ApplySort(
        IQueryable<Vehicle> query,
        string? sortBy,
        string sortDir)
    {
        var descending = sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);
        return sortBy?.Trim().ToLowerInvariant() switch
        {
            "totalseats" => descending ? query.OrderByDescending(x => x.TotalSeats) : query.OrderBy(x => x.TotalSeats),
            "status" => descending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            "isactive" => descending ? query.OrderByDescending(x => x.IsActive) : query.OrderBy(x => x.IsActive),
            "createdat" => descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            "updatedat" => descending ? query.OrderByDescending(x => x.UpdatedAt) : query.OrderBy(x => x.UpdatedAt),
            _ => descending ? query.OrderByDescending(x => x.LicensePlate) : query.OrderBy(x => x.LicensePlate),
        };
    }
}
