using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class TripAuditLogPersistenceTests
{
    [Fact]
    public void Model_MatchesAppendOnlyAuditContract()
    {
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var dbContext = new TripDbContext(options, new SystemClock());
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(TripAuditLog));

        Assert.NotNull(entity);
        Assert.Equal("trip_audit_logs", entity.GetTableName());
        Assert.Equal("vietride_trip", entity.GetSchema());
        Assert.Equal(
            new[] { "id", "trip_id", "actor_user_id", "action", "metadata", "occurred_at", "created_at" }.OrderBy(name => name),
            entity.GetProperties().Select(property => property.GetColumnName()).OrderBy(name => name));

        var createdAt = entity.FindProperty(nameof(TripAuditLog.CreatedAt));
        Assert.Equal("now()", createdAt?.GetDefaultValueSql());
        Assert.Equal("jsonb", entity.FindProperty(nameof(TripAuditLog.Metadata))?.GetColumnType());
        Assert.Equal(64, entity.FindProperty(nameof(TripAuditLog.Action))?.GetMaxLength());

        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(nameof(TripAuditLog.TripId), Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);

        var indexes = entity.GetIndexes().ToDictionary(index => index.GetDatabaseName()!);
        Assert.Equal(3, indexes.Count);
        Assert.Equal(new[] { false, true }, indexes["idx_trip_audit_logs_trip_occurred"].IsDescending);
        Assert.Equal(new[] { false, true }, indexes["idx_trip_audit_logs_actor_occurred"].IsDescending);
        Assert.Equal("actor_user_id IS NOT NULL", indexes["idx_trip_audit_logs_actor_occurred"].GetFilter());
        Assert.Equal(new[] { false, true }, indexes["idx_trip_audit_logs_action_occurred"].IsDescending);
    }

    [Fact]
    public void RepositorySurface_ExposesInsertAndReadOnly()
    {
        var methods = typeof(ITripAuditLogRepository)
            .GetMethods()
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { "AddAsync", "ListByTripIdAsync" }, methods);
    }
}
