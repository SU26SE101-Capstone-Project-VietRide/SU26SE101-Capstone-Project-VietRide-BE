using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure.Persistence.Configurations;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.Inbox;
using VietRide.Shared.Persistence.Outbox;

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
        builder.MapEnum<UserLockSource>("user_lock_source", PostgresEnumNameTranslator);
        builder.MapEnum<EmailVerificationPurpose>("email_verification_purpose", PostgresEnumNameTranslator);
        builder.MapEnum<OAuthProvider>("oauth_provider", PostgresEnumNameTranslator);
        builder.MapEnum<RefreshTokenRevokeReason>("refresh_token_revoke_reason", PostgresEnumNameTranslator);
        builder.MapEnum<DevicePlatform>("device_platform", PostgresEnumNameTranslator);
        builder.MapEnum<ActivityLogAction>("activity_log_action", PostgresEnumNameTranslator);
        builder.MapEnum<OperatorRegistrationStatus>("operator_registration_status", PostgresEnumNameTranslator);
        builder.MapEnum<SubscriptionStatus>("subscription_status", PostgresEnumNameTranslator);
        builder.MapEnum<SubscriptionPaymentMethod>("subscription_payment_method", PostgresEnumNameTranslator);
        builder.MapEnum<SubscriptionBillingPeriod>("subscription_billing_period", PostgresEnumNameTranslator);
        builder.MapEnum<SubscriptionUpgradeAttemptStatus>("subscription_upgrade_attempt_status", PostgresEnumNameTranslator);
        builder.MapEnum<OutboxEventStatus>("outbox_event_status", PostgresEnumNameTranslator);
    }

    // --- DbSets ---

    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<OperatorWalletBackfillMarker> OperatorWalletBackfillMarkers => Set<OperatorWalletBackfillMarker>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<OperatorSubscription> OperatorSubscriptions => Set<OperatorSubscription>();
    public DbSet<SubscriptionUpgradeAttempt> SubscriptionUpgradeAttempts => Set<SubscriptionUpgradeAttempt>();
    public DbSet<SubscriptionQuotaAllocation> SubscriptionQuotaAllocations => Set<SubscriptionQuotaAllocation>();
    public DbSet<SubscriptionUsageWarningMarker> SubscriptionUsageWarningMarkers => Set<SubscriptionUsageWarningMarker>();

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
        modelBuilder.HasPostgresEnum("user_lock_source", Enum.GetNames<UserLockSource>());
        modelBuilder.HasPostgresEnum("email_verification_purpose", Enum.GetNames<EmailVerificationPurpose>());
        modelBuilder.HasPostgresEnum("oauth_provider", Enum.GetNames<OAuthProvider>());
        modelBuilder.HasPostgresEnum("refresh_token_revoke_reason", Enum.GetNames<RefreshTokenRevokeReason>());
        modelBuilder.HasPostgresEnum("device_platform", Enum.GetNames<DevicePlatform>());
        modelBuilder.HasPostgresEnum("activity_log_action", Enum.GetNames<ActivityLogAction>());
        modelBuilder.HasPostgresEnum("operator_registration_status", Enum.GetNames<OperatorRegistrationStatus>());
        modelBuilder.HasPostgresEnum("subscription_status", Enum.GetNames<SubscriptionStatus>());
        modelBuilder.HasPostgresEnum("subscription_payment_method", Enum.GetNames<SubscriptionPaymentMethod>());
        modelBuilder.HasPostgresEnum("subscription_billing_period", Enum.GetNames<SubscriptionBillingPeriod>());
        modelBuilder.HasPostgresEnum("subscription_upgrade_attempt_status", Enum.GetNames<SubscriptionUpgradeAttemptStatus>());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        RegisterPostgresEnums(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> defined in this assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        modelBuilder.AddVietRideIntegrationInbox();

        // Base applies snake_case naming + OutboxEvents mapping.
        base.OnModelCreating(modelBuilder);
    }
}
