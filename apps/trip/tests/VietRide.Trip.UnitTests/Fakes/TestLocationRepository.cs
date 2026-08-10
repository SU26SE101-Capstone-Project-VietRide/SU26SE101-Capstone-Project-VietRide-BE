using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Fakes;

internal sealed class TestLocationRepository : ILocationRepository
{
    private readonly List<Location> locations;

    private TestLocationRepository(Location parent, Location leaf)
    {
        Parent = parent;
        Leaf = leaf;
        locations = [parent, leaf];
    }

    private TestLocationRepository(IEnumerable<Location> seed)
    {
        locations = seed.ToList();
        Parent = locations.First(location => Location.IsTopLevelType(location.Type));
        Leaf = locations.First(location => Location.IsLeafType(location.Type));
    }

    public Location Parent { get; }
    public Location Leaf { get; }

    public static TestLocationRepository Create(string leafCode = "20195")
    {
        var parent = Location.Create("48", "Thành phố Đà Nẵng", Location.MunicipalityType, 1);
        var leaf = Location.Create(leafCode, "Phường Hải Châu", Location.WardType, parent.Id, 1);
        return new TestLocationRepository(parent, leaf);
    }

    public static TestLocationRepository From(params Location[] locations) => new(locations);

    public Task<Location?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(locations.FirstOrDefault(location => location.Id == id));
    public Task<Location> AddAsync(Location entity, CancellationToken ct)
    {
        locations.Add(entity);
        return Task.FromResult(entity);
    }
    public void Update(Location entity) { }
    public void Remove(Location entity) => locations.Remove(entity);
    public IQueryable<Location> Query() => locations.AsQueryable();
    public IQueryable<Location> QueryNoTracking() => locations.AsQueryable();
    public Task<Location?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(locations.FirstOrDefault(location => location.Id == id && location.IsActive));
    public Task<Location?> GetActiveByCodeAsync(string code, CancellationToken cancellationToken)
        => Task.FromResult(locations.FirstOrDefault(location => location.Code == code.Trim() && location.IsActive));
    public Task<bool> ExistsByCodeAsync(string code, Guid? exceptId, CancellationToken cancellationToken)
        => Task.FromResult(locations.Any(location => location.Code == code.Trim()
            && (!exceptId.HasValue || location.Id != exceptId.Value)));
    public Task<PagedResult<Location>> ListAsync(
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken)
        => Task.FromResult(PagedResult<Location>.Create(locations, page, pageSize, locations.Count));
}
