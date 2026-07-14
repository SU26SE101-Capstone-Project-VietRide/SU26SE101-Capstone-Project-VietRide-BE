using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Booking.Application.Abstractions.Repositories;

public interface IBookingPendingActionRepository : IRepository<BookingPendingAction, Guid> { }
