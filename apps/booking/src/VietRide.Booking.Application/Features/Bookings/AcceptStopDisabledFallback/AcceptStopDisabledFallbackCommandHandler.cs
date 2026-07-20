using System.Text.Json;
using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Bookings.AcceptStopDisabledFallback;

public sealed class AcceptStopDisabledFallbackCommandHandler(
    IBookingPendingActionRepository pendingActions,
    IBookingRepository bookings,
    IBookingStationCanonicalizer stationCanonicalizer,
    IClock clock) : IRequestHandler<AcceptStopDisabledFallbackCommand, AcceptStopDisabledFallbackResult>
{
    public async Task<AcceptStopDisabledFallbackResult> Handle(
        AcceptStopDisabledFallbackCommand request,
        CancellationToken ct)
    {
        var action = await pendingActions.GetByIdForUpdateAsync(request.ActionId, ct)
                ?? throw new CodedNotFoundException("BOOKING_PENDING_ACTION_NOT_FOUND", "Booking pending action was not found.");
        var booking = await bookings.FindByIdForUpdateAsync(request.BookingId, ct)
                ?? throw new CodedNotFoundException("BOOKING_NOT_FOUND", "Booking not found.");
        if (booking.PassengerUserId != request.PassengerUserId || action.BookingId != booking.Id)
            throw new CodedNotFoundException("BOOKING_NOT_FOUND", "Booking not found.");
        if (action.Reason != BookingPendingActionReason.STOP_DISABLED)
            throw new CodedConflictException("BOOKING_PENDING_ACTION_NOT_RESOLVABLE", "Pending action cannot be resolved by this endpoint.");
        if (action.ResolvedAt.HasValue || action.ResolvedAction.HasValue)
            throw new CodedConflictException("BOOKING_PENDING_ACTION_ALREADY_RESOLVED", "Booking pending action was already resolved.");
        var now = clock.UtcNow;
        if (action.Deadline < now)
            throw new CodedConflictException("BOOKING_PENDING_ACTION_EXPIRED", "Booking pending action has expired.");
        JsonElement root;
        try { root = JsonDocument.Parse(action.Metadata ?? "{}").RootElement.Clone(); }
        catch (JsonException) { throw new CodedConflictException("BOOKING_PENDING_ACTION_NOT_RESOLVABLE", "Pending action metadata is invalid."); }
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("affectedField", out var fieldElement)
            || !root.TryGetProperty("fallbackStationId", out var fallbackElement)
            || fieldElement.ValueKind != JsonValueKind.String
            || !fallbackElement.TryGetGuid(out var fallback))
            throw new CodedConflictException("BOOKING_PENDING_ACTION_NOT_RESOLVABLE", "Pending action metadata is invalid.");
        var canonicalization = await stationCanonicalizer.LockAndResolveAsync(
            BookingStationCanonicalization.Collect(fallback),
            ct);
        fallback = canonicalization.Resolve(fallback);
        var field = fieldElement.GetString();
        if (field == "PICKUP") booking.ChangePickup(fallback, null);
        else if (field == "DROPOFF") booking.ChangeDropoff(fallback, null);
        else throw new CodedConflictException("BOOKING_PENDING_ACTION_NOT_RESOLVABLE", "Pending action metadata is invalid.");
        action.Resolve(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION, now);
        bookings.Update(booking);
        pendingActions.Update(action);
        return new AcceptStopDisabledFallbackResult(booking.Id, action.Id, action.ResolvedAction!.Value.ToString(), now);
    }
}
