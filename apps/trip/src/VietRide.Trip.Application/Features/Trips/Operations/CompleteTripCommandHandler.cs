using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class CompleteTripCommandHandler
    : IRequestHandler<CompleteTripCommand, CompleteTripResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITripRepository _trips;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<CompleteTripCommandHandler> _logger;
    private readonly IRouteChangeProposalLifecycleService? _routeChangeProposals;

    public CompleteTripCommandHandler(
        ITripRepository trips,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<CompleteTripCommandHandler> logger,
        IRouteChangeProposalLifecycleService? routeChangeProposals = null)
    {
        _trips = trips;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
        _routeChangeProposals = routeChangeProposals;
    }

    public async Task<CompleteTripResponse> Handle(
        CompleteTripCommand request,
        CancellationToken cancellationToken)
    {
        var trip = await _trips.GetForUpdateAsync(request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

        if (trip.Status != TripStatus.IN_PROGRESS)
        {
            throw new ConflictException(
                "TRIP_ALREADY_TERMINAL",
                "Only an in-progress trip can be completed.");
        }

        if (!request.IsAutomatic)
        {
            if (!request.ActorUserId.HasValue
                || (trip.DriverUserId != request.ActorUserId.Value
                    && trip.AssistantUserId != request.ActorUserId.Value))
            {
                throw new ForbiddenException(
                    "FORBIDDEN",
                    "Only the assigned driver or assistant can complete this trip.");
            }
        }

        var now = _clock.UtcNow;
        if (request.IsAutomatic)
            trip.CompleteAutomatically(now);
        else
            trip.CompleteManually(now, request.ActorUserId!.Value);
        if (_routeChangeProposals is not null)
            await _routeChangeProposals.ExpirePendingForTripAsync(trip.Id, now, cancellationToken);

        var evt = new TripCompletedIntegrationEvent(
            trip.Id,
            trip.OperatorId,
            now,
            trip.HasSubstitution,
            trip.TripCode);
        await _outbox.EnqueueAsync(
            evt.EventType,
            JsonSerializer.Serialize(evt, JsonOptions),
            cancellationToken);

        _logger.LogInformation(
            "{AuditAction}: Trip {TripId} completed at {CompletedAt} by {ActorUserId}.",
            request.IsAutomatic ? "TRIP_COMPLETED_FALLBACK" : "TRIP_COMPLETED_MANUAL",
            trip.Id,
            now,
            request.ActorUserId);

        return new CompleteTripResponse(trip.Id, trip.Status.ToString(), now);
    }
}
