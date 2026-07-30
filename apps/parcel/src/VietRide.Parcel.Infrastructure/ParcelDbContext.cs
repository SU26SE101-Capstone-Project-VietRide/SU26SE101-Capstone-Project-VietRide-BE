using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.Inbox;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure;

public sealed class ParcelDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_parcel";

    public ParcelDbContext(DbContextOptions<ParcelDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    public DbSet<ParcelEntity> Parcels => Set<ParcelEntity>();
    public DbSet<ParcelDeliveryToken> ParcelDeliveryTokens => Set<ParcelDeliveryToken>();
    public DbSet<ParcelCargoRecoveryOperation> ParcelCargoRecoveryOperations
        => Set<ParcelCargoRecoveryOperation>();
    public DbSet<ParcelRouteFare> ParcelRouteFares => Set<ParcelRouteFare>();
    public DbSet<ParcelStats> ParcelStats => Set<ParcelStats>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
    public DbSet<OperatorDepositPolicy> OperatorDepositPolicies => Set<OperatorDepositPolicy>();

    public static void ConfigurePostgresTypes(NpgsqlDataSourceBuilder dataSourceBuilder)
    {
        var translator = new NpgsqlNullNameTranslator();
        dataSourceBuilder.MapEnum<ParcelStatus>($"{SchemaName}.parcel_status", translator);
        dataSourceBuilder.MapEnum<ParcelSizeCategory>($"{SchemaName}.parcel_size_category", translator);
        dataSourceBuilder.MapEnum<ParcelReviewDecision>($"{SchemaName}.parcel_review_decision", translator);
        dataSourceBuilder.MapEnum<ParcelDeliveryMethod>($"{SchemaName}.parcel_delivery_method", translator);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.HasPostgresEnum(SchemaName, "parcel_status", Enum.GetNames<ParcelStatus>());
        modelBuilder.HasPostgresEnum(SchemaName, "parcel_size_category", Enum.GetNames<ParcelSizeCategory>());
        modelBuilder.HasPostgresEnum(SchemaName, "parcel_review_decision", Enum.GetNames<ParcelReviewDecision>());
        modelBuilder.HasPostgresEnum(SchemaName, "parcel_delivery_method", Enum.GetNames<ParcelDeliveryMethod>());

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ParcelDbContext).Assembly);
        modelBuilder.AddVietRideIntegrationInbox();

        base.OnModelCreating(modelBuilder);
    }
}
