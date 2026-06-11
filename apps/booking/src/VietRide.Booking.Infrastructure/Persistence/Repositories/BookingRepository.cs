using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Repositories;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository for the Booking aggregate.
/// Implements <see cref="IBookingRepository"/> — extends the generic repository contract
/// (<see cref="IRepository{TEntity,TId}"/>) with Booking-specific queries.
/// </summary>
internal sealed class BookingRepository : IBookingRepository
{
    private readonly BookingDbContext _db;

    public BookingRepository(BookingDbContext db)
    {
        _db = db;
    }

    // -----------------------------------------------------------------------
    // IRepository<Booking, Guid>
    // -----------------------------------------------------------------------

    public async Task<BookingEntity?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<BookingEntity> AddAsync(BookingEntity entity, CancellationToken ct)
    {
        await _db.Bookings.AddAsync(entity, ct);
        return entity;
    }

    public void Update(BookingEntity entity)
        => _db.Bookings.Update(entity);

    public void Remove(BookingEntity entity)
        => _db.Bookings.Remove(entity);

    public IQueryable<BookingEntity> Query()
        => _db.Bookings;

    public IQueryable<BookingEntity> QueryNoTracking()
        => _db.Bookings.AsNoTracking();

    // -----------------------------------------------------------------------
    // IBookingRepository — aggregate-specific queries
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<BookingEntity?> FindByBookingCodeAsync(
        string bookingCode,
        CancellationToken ct = default)
    {
        // Compare the converted string primitive directly to avoid fragile EF translation
        // of struct equality through a value converter.
        return await _db.Bookings
            .FirstOrDefaultAsync(
                b => EF.Property<string>(b, "booking_code") == bookingCode,
                ct);
    }

    /// <inheritdoc/>
    public async Task<BookingEntity?> FindByIdWithPassengersAsync(
        Guid bookingId,
        CancellationToken ct = default)
        => await _db.Bookings
            .Include(b => b.Passengers)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
}
