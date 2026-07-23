using System.Text.Json;
using Hangfire;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Infrastructure.Jobs;

public sealed class RouteChangeExpiryJob(
    IBookingPendingActionRepository pendingActions,
    IBookingRepository bookings,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Queue("booking")]
    [AutomaticRetry(Attempts = 5)]
    public async Task ExecuteAsync(Guid pendingActionId, CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var action = await pendingActions.GetByIdForUpdateAsync(pendingActionId, cancellationToken);
            if (action is null
                || action.Reason != BookingPendingActionReason.ROUTE_CHANGE
                || action.ResolvedAt.HasValue
                || action.Deadline >= clock.UtcNow)
            {
                return false;
            }

            var booking = await bookings.FindByIdForUpdateAsync(action.BookingId, cancellationToken);
            if (booking is null || booking.Status != BookingStatus.CONFIRMED)
            {
                return false;
            }

            var now = clock.UtcNow;
            var fallback = ParseFallbackMetadata(action.Metadata);
            action.AutoFallbackRouteChange(now);
            pendingActions.Update(action);

            var eventId = Guid.NewGuid();
            var applied = new BookingRouteChangeAutoFallbackAppliedIntegrationEvent(
                eventId,
                now,
                booking.Id,
                booking.TripId,
                booking.PassengerUserId,
                action.Id,
                fallback.OriginalStopId,
                fallback.FallbackDestinationStationId);
            await outbox.EnqueueAsync(
                eventId,
                BookingRouteChangeAutoFallbackAppliedIntegrationEvent.EventTypeValue,
                JsonSerializer.Serialize(applied, JsonOptions),
                cancellationToken);
            return true;
        }, cancellationToken);
    }

    private static (Guid OriginalStopId, Guid FallbackDestinationStationId)
        ParseFallbackMetadata(string? metadata)
    {
        try
        {
            using var document = JsonDocument.Parse(metadata ?? string.Empty);
            var root = document.RootElement;
            var originalStopId = root.GetProperty("originalStopId").GetGuid();
            var fallbackDestinationStationId =
                root.GetProperty("fallbackDestinationStationId").GetGuid();
            if (originalStopId == Guid.Empty
                || fallbackDestinationStationId == Guid.Empty
                || !root.GetProperty("shuttleRequired").GetBoolean())
            {
                throw new InvalidOperationException();
            }

            return (originalStopId, fallbackDestinationStationId);
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidOperationException
            or FormatException
            or KeyNotFoundException)
        {
            throw new InvalidOperationException(
                "Route-change fallback metadata is invalid.",
                exception);
        }
    }
}
