using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure.Persistence.Configurations;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;

namespace VietRide.Identity.Infrastructure;

/// Identity service EF Core context — owns schema <c>vietride_identity</c>.
public sealed class IdentityDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_identity";

    private static readonly INpgsqlNameTranslator PostgresEnumNameTranslator = new NpgsqlNullNameTranslator();

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    public static void ConfigurePostgresEnums(NpgsqlDataSourceBuilder builder)
    {
        builder.MapEnum<UserRole>("user_role", PostgresEnumNameTranslator);
        builder.MapEnum<UserStatus>("user_status", PostgresEnumNameTranslator);
        builder.MapEnum<EmailVerificationPurpose>("email_verification_purpose", PostgresEnumNameTranslator);
        builder.MapEnum<OAuthProvider>("oauth_provider", PostgresEnumNameTranslator);
        builder.MapEnum<RefreshTokenRevokeReason>("refresh_token_revoke_reason", PostgresEnumNameTranslator);
        builder.MapEnum<DevicePlatform>("device_platform", PostgresEnumNameTranslator);
        builder.MapEnum<ActivityLogAction>("activity_log_action", PostgresEnumNameTranslator);
    }

    // --- DbSets ---

    /// <summary>Day-3 stub — PK only; Day 6 adds full columns + behavior.</summary>
    public DbSet<Operator> Operators => Set<Operator>();

    public DbSet<User> Users => Set<User>();
    public DbSet<OAuthIdentity> OAuthIdentities => Set<OAuthIdentity>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    private static void RegisterPostgresEnums(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum("user_role", Enum.GetNames<UserRole>());
        modelBuilder.HasPostgresEnum("user_status", Enum.GetNames<UserStatus>());
        modelBuilder.HasPostgresEnum("email_verification_purpose", Enum.GetNames<EmailVerificationPurpose>());
        modelBuilder.HasPostgresEnum("oauth_provider", Enum.GetNames<OAuthProvider>());
        modelBuilder.HasPostgresEnum("refresh_token_revoke_reason", Enum.GetNames<RefreshTokenRevokeReason>());
        modelBuilder.HasPostgresEnum("device_platform", Enum.GetNames<DevicePlatform>());
        modelBuilder.HasPostgresEnum("activity_log_action", Enum.GetNames<ActivityLogAction>());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        RegisterPostgresEnums(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> defined in this assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        // Base applies snake_case naming + OutboxMessages mapping.
        base.OnModelCreating(modelBuilder);
    }
}
