using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.UnitOfWork;

namespace VietRide.Identity.UnitTests.Persistence;

/// <summary>
/// Persistence proof A4(2): verifies that <see cref="EfUnitOfWork"/> delegates
/// <see cref="EfUnitOfWork.SaveChangesAsync"/> to the underlying
/// <see cref="VietRideDbContextBase.SaveChangesAsync"/>, and that the
/// Begin/Commit/Rollback contract is upheld.
/// Uses a concrete test subclass of <see cref="VietRideDbContextBase"/> whose
/// SaveChangesAsync is intercepted — no real database required.
/// </summary>
public sealed class EfUnitOfWorkTests
{
    // -----------------------------------------------------------------------
    // Test seam — concrete subclass with observable SaveChanges delegation
    // -----------------------------------------------------------------------

    private sealed class SpyDbContext : VietRideDbContextBase
    {
        public int SaveChangesCallCount { get; private set; }

        public SpyDbContext(DbContextOptions options, IClock clock)
            : base(options, clock)
        {
        }

        /// Override to count calls without touching a real DB.
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class AuditingDbContext : VietRideDbContextBase
    {
        public AuditingDbContext(DbContextOptions options, IClock clock)
            : base(options, clock)
        {
        }

        public DbSet<AuditedEntity> AuditedEntities => Set<AuditedEntity>();
    }

    private sealed class AuditedEntity : BaseEntity<Guid>
    {
        public string Name { get; private set; } = string.Empty;

        public static AuditedEntity Create(string name)
            => new()
            {
                Id = Guid.NewGuid(),
                Name = name,
            };

        public void Rename(string name)
            => Name = name;
    }

    private static (SpyDbContext db, EfUnitOfWork uow) Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        // Use bare DbContextOptions with no provider — SpyDbContext overrides SaveChangesAsync
        // and does NOT call base, so no real database is ever accessed.
        // DbContext(DbContextOptions) does not throw on construction; it only throws
        // when a provider-dependent operation (e.g. Database.BeginTransactionAsync) is invoked.
        var options = new DbContextOptions<SpyDbContext>();

        var db = new SpyDbContext(options, clock);
        var uow = new EfUnitOfWork(db);
        return (db, uow);
    }

    private static AuditingDbContext BuildAuditingContext(DateTimeOffset now)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        var options = new DbContextOptionsBuilder<AuditingDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=vietride_audit_test;Username=test;Password=test")
            .Options;

        return new AuditingDbContext(options, clock);
    }

    private static IdentityDbContext BuildIdentityContext()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=vietride_identity_model_test;Username=test;Password=test")
            .Options;

        return new IdentityDbContext(options, clock);
    }

    // -----------------------------------------------------------------------
    // Happy-path: BaseEntity auditing through VietRideDbContextBase
    // -----------------------------------------------------------------------

    [Fact]
    public void BaseEntity_ImplementsIAuditable()
    {
        typeof(IAuditable).IsAssignableFrom(typeof(BaseEntity<Guid>))
            .Should().BeTrue("VietRideDbContextBase scans ChangeTracker.Entries<IAuditable>() for audit columns.");
    }

    [Fact]
    public void IdentityModel_EmailVerificationToken_DoesNotMapUpdatedAtColumn()
    {
        // Arrange
        using var db = BuildIdentityContext();

        // Act
        var entityType = db.Model.FindEntityType(typeof(EmailVerificationToken));

        // Assert — schema.sql has created_at only for email_verification_tokens.
        entityType.Should().NotBeNull();
        entityType!.FindProperty(nameof(EmailVerificationToken.CreatedAt)).Should().NotBeNull();
        entityType.FindProperty(nameof(EmailVerificationToken.UpdatedAt)).Should().BeNull(
            "email_verification_tokens intentionally ignores BaseEntity.UpdatedAt to avoid DB column drift.");
    }

    [Fact]
    public void SaveChanges_AuditsAddedBaseEntity_BeforeProviderSave()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);
        using var db = BuildAuditingContext(now);
        var entity = AuditedEntity.Create("created");

        db.AuditedEntities.Add(entity);

        // Act — the database points to a closed local port, so EF save fails after auditing runs.
        var act = () => db.SaveChanges();

        // Assert
        act.Should().Throw<Exception>();
        entity.CreatedAt.Should().Be(now);
        entity.UpdatedAt.Should().Be(now);
    }

    [Fact]
    public void SaveChanges_AuditsModifiedBaseEntity_BeforeProviderSave()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 6, 3, 12, 30, 0, TimeSpan.Zero);
        using var db = BuildAuditingContext(now);
        var createdAt = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);
        var entity = AuditedEntity.Create("original");
        entity.CreatedAt = createdAt;
        entity.UpdatedAt = createdAt;

        db.Attach(entity);
        entity.Rename("updated");
        db.Entry(entity).State = EntityState.Modified;

        // Act — the database points to a closed local port, so EF save fails after auditing runs.
        var act = () => db.SaveChanges();

        // Assert
        act.Should().Throw<Exception>();
        entity.CreatedAt.Should().Be(createdAt);
        entity.UpdatedAt.Should().Be(now);
    }

    // -----------------------------------------------------------------------
    // Happy-path: SaveChangesAsync delegation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SaveChangesAsync_DelegatesToDbContext_SaveChangesAsync()
    {
        // Arrange
        var (db, uow) = Build();
        db.SaveChangesCallCount.Should().Be(0, "no saves yet");

        // Act
        await uow.SaveChangesAsync(CancellationToken.None);

        // Assert — EfUnitOfWork.SaveChangesAsync must delegate to db.SaveChangesAsync.
        db.SaveChangesCallCount.Should().Be(1,
            "EfUnitOfWork.SaveChangesAsync must delegate to VietRideDbContextBase.SaveChangesAsync.");
    }

    // -----------------------------------------------------------------------
    // Error-case: CommitAsync without BeginTransactionAsync → clear exception
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CommitAsync_WithoutBeginTransaction_ThrowsInvalidOperationException()
    {
        // Arrange
        var (_, uow) = Build();

        // Act
        var act = async () => await uow.CommitAsync(CancellationToken.None);

        // Assert — CommitAsync with no active transaction must throw a clear InvalidOperationException
        // (per EfUnitOfWork contract: "CommitAsync called without an active transaction").
        await act.Should().ThrowAsync<InvalidOperationException>(
            "CommitAsync with no prior BeginTransactionAsync must throw InvalidOperationException.");
    }

    // -----------------------------------------------------------------------
    // Error-case: RollbackAsync without BeginTransactionAsync → safe no-op
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RollbackAsync_WithNoActiveTransaction_IsNoOp()
    {
        // Arrange
        var (_, uow) = Build();

        // Act + Assert — must complete without throwing.
        var act = async () => await uow.RollbackAsync(CancellationToken.None);
        await act.Should().NotThrowAsync(
            "RollbackAsync with no active transaction must be a safe no-op per EfUnitOfWork contract.");
    }
}
