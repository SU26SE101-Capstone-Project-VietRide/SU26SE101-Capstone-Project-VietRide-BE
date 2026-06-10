using MediatR;
using VietRide.Shared.Application.Pagination;

namespace VietRide.Identity.Application.Features.Passenger.GetPassengerBookings;

/// <summary>
/// STUB handler for GET /v1/passenger/bookings.
/// Returns the canonical empty paginated envelope (items empty, page 1, pageSize 20, total 0).
/// Booking item schema is NOT defined this day — finalized in Sprint 3 (SCV-76 / Booking).
/// No repository, no booking data.
/// </summary>
public sealed class GetPassengerBookingsQueryHandler
    : IRequestHandler<GetPassengerBookingsQuery, PagedResult<object>>
{
    public Task<PagedResult<object>> Handle(
        GetPassengerBookingsQuery request,
        CancellationToken cancellationToken)
    {
        var result = new PagedResult<object>(
            Items: Array.Empty<object>(),
            Total: 0,
            Page: 1,
            PageSize: 20);

        return Task.FromResult(result);
    }
}
