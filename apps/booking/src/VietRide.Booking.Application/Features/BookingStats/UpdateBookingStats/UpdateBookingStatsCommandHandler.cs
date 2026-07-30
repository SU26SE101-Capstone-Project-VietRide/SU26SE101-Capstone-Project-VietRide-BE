using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.BookingStats.UpdateBookingStats;

public sealed class UpdateBookingStatsCommandHandler
    : IRequestHandler<UpdateBookingStatsCommand, bool>
{
    private static readonly TimeSpan IctOffset = TimeSpan.FromHours(7);

    private readonly IBookingRepository _bookings;
    private readonly IBookingStatsRepository _stats;
    private readonly IOperatorServiceClient _operatorClient;
    private readonly ILogger<UpdateBookingStatsCommandHandler> _logger;

    public UpdateBookingStatsCommandHandler(
        IBookingRepository bookings,
        IBookingStatsRepository stats,
        IOperatorServiceClient operatorClient,
        ILogger<UpdateBookingStatsCommandHandler> logger)
    {
        _bookings = bookings;
        _stats = stats;
        _operatorClient = operatorClient;
        _logger = logger;
    }

    public async Task<bool> Handle(
        UpdateBookingStatsCommand request,
        CancellationToken cancellationToken)
    {
        var claimed = await _stats.TryClaimProcessedEventAsync(
            request.EventType,
            request.DedupeId ?? request.BookingId,
            DateTimeOffset.UtcNow,
            cancellationToken);

        if (!claimed)
        {
            return false;
        }

        var booking = await _bookings.FindByIdWithPassengersAsync(request.BookingId, cancellationToken);
        if (booking is null)
        {
            _logger.LogWarning(
                "BookingStats event {EventType} cannot be applied because booking {BookingId} was not found.",
                request.EventType,
                request.BookingId);
            throw new InvalidOperationException(
                $"BookingStats event {request.EventType} references missing booking {request.BookingId}.");
        }

        var statDate = ResolveStatDate(booking, request.Transition);
        var operatorName = await TryGetOperatorNameAsync(booking.OperatorId, cancellationToken);

        var delta = global::VietRide.Booking.Domain.Entities.BookingStats.Create(
            booking.OperatorId,
            statDate,
            booking.TripId,
            operatorName);
        ApplyTransition(delta, booking.Passengers.Count, request);

        await _stats.UpsertDeltaAsync(delta, cancellationToken);
        return true;
    }

    private async Task<string?> TryGetOperatorNameAsync(
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        try
        {
            var lookup = await _operatorClient.GetOperatorAsync(operatorId, cancellationToken);
            return lookup?.Name;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Operator lookup failed while updating BookingStats for operator {OperatorId}.",
                operatorId);
            return null;
        }
    }

    private static DateOnly ResolveStatDate(
        global::VietRide.Booking.Domain.Entities.Booking booking,
        BookingStatsTransition transition)
    {
        var timestamp = transition switch
        {
            BookingStatsTransition.Confirmed => booking.ConfirmedAt,
            BookingStatsTransition.Cancelled => booking.CancelledAt,
            BookingStatsTransition.Refunded => booking.RefundedAt,
            _ => null,
        };

        return DateOnly.FromDateTime((timestamp ?? DateTimeOffset.UtcNow).ToOffset(IctOffset).DateTime);
    }

    private static void ApplyTransition(
        global::VietRide.Booking.Domain.Entities.BookingStats delta,
        int seatCount,
        UpdateBookingStatsCommand request)
    {
        var totalBookings = 0;
        var totalConfirmed = 0;
        var totalCancelled = 0;
        var totalNoShow = 0;
        var totalCompleted = 0;
        var totalRevenue = 0L;
        var totalRefunded = 0L;
        var totalSeatsBooked = 0;

        switch (request.Transition)
        {
            case BookingStatsTransition.Confirmed:
                totalBookings += 1;
                totalConfirmed += 1;
                totalRevenue += request.Amount;
                totalSeatsBooked += seatCount;
                break;
            case BookingStatsTransition.Cancelled:
                totalCancelled += 1;
                break;
            case BookingStatsTransition.Refunded:
                totalRefunded += request.Amount;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Transition, "Unsupported booking stats transition.");
        }

        delta.SetCounters(
            totalBookings,
            totalConfirmed,
            totalCancelled,
            totalNoShow,
            totalCompleted,
            Money.FromRaw(totalRevenue),
            Money.FromRaw(totalRefunded),
            totalSeatsBooked);
    }
}
