using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Application.Pagination;

namespace VietRide.Identity.Application.Features.Passenger.GetPassengerBookings;

/// <summary>
/// Query for GET /v1/passenger/bookings.
/// STUB — item schema finalized in Sprint 3 (SCV-76 / Booking).
/// </summary>
public sealed record GetPassengerBookingsQuery(Guid UserId) : IQuery<PagedResult<object>>;
