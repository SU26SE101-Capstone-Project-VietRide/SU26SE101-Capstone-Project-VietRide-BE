using VietRide.Shared.Application.Repositories;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Application.Abstractions.Repositories;

/// <summary>
/// Repository contract for the Booking aggregate.
/// Extends <see cref="IRepository{TEntity,TId}"/> with Booking-specific queries.
/// </summary>
public interface IBookingRepository : IRepository<BookingEntity, Guid>
{
    /// <summary>
    /// Finds a booking by its booking code string (unique index).
    /// </summary>
    Task<BookingEntity?> FindByBookingCodeAsync(string bookingCode, CancellationToken ct = default);

    /// <summary>
    /// Returns a booking with Passengers eagerly loaded.
    /// Used for saga compensation checks and cancellation.
    /// </summary>
    Task<BookingEntity?> FindByIdWithPassengersAsync(Guid bookingId, CancellationToken ct = default);
}
