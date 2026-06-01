using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VietRide.Shared.Kernel.Abstractions;
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
