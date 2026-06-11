using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Services;

/// <summary>
/// Orchestration service for cross-handler Booking business logic.
/// <para>
/// Implements <see cref="IBookingService"/>. This day only IBookingRepository,
/// ITripServiceClient, and IClock are injected — IVoucherRepository is deferred to Day 14
/// (per BSOT §3.2.5 line 657 FINAL constructor comment).
/// </para>
/// <para>
/// <see cref="_bookingRepo"/> and <see cref="_clock"/> are injected now and will be consumed
/// by Day-14 voucher validation and Day-17 cancel/refund logic respectively.
/// </para>
/// </summary>
public sealed class BookingService : IBookingService
{
    // _bookingRepo and _clock consumed by Day-14 voucher + Day-17 cancel logic.
#pragma warning disable IDE0052
    private readonly IBookingRepository _bookingRepo;
    private readonly IClock _clock;
#pragma warning restore IDE0052
    private readonly ITripServiceClient _tripClient;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        IBookingRepository bookingRepo,
        ITripServiceClient tripClient,
        IClock clock,
        ILogger<BookingService> logger)
    {
        _bookingRepo = bookingRepo;
        _tripClient = tripClient;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ReleaseSeatsAsync(
        Guid tripId,
        Guid seatLockToken,
        IReadOnlyList<string> seatNumbers,
        CancellationToken ct = default)
    {
        try
        {
            await _tripClient.ReleaseSeatsAsync(
                tripId,
                seatLockToken,
                seatNumbers,
                ct);

            _logger.LogInformation(
                "Released seats {SeatNumbers} for trip {TripId} (lock token {Token}).",
                string.Join(",", seatNumbers),
                tripId,
                seatLockToken);
        }
        catch (Exception ex)
        {
            // Release is best-effort; log and continue (saga compensation should not re-throw)
            _logger.LogError(
                ex,
                "Failed to release seats {SeatNumbers} for trip {TripId} (lock token {Token}).",
                string.Join(",", seatNumbers),
                tripId,
                seatLockToken);
        }
    }
}
