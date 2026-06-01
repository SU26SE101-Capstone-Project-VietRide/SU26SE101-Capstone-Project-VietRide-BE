using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Infrastructure.Persistence.Configurations;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;

namespace VietRide.Identity.Infrastructure;

/// Identity service EF Core context — owns schema <c>vietride_identity</c>.
public sealed class IdentityDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_identity";

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    // --- DbSets ---

    /// <summary>Day-3 stub — PK only; Day 6 adds full columns + behavior.</summary>
    public DbSet<Operator> Operators => Set<Operator>();

    public DbSet<User> Users => Set<User>();
    public DbSet<OAuthIdentity> OAuthIdentities => Set<OAuthIdentity>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        // Apply all IEntityTypeConfiguration<T> defined in this assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        // Base applies snake_case naming + OutboxMessages mapping.
        base.OnModelCreating(modelBuilder);
    }
}
