using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class AutoCompletedFallbackJob
{
    private readonly ITripRepository _trips;
    private readonly ISender _sender;
    private readonly IClock _clock;
    private readonly ILogger<AutoCompletedFallbackJob> _logger;

    public AutoCompletedFallbackJob(
        ITripRepository trips,
        ISender sender,
        IClock clock,
        ILogger<AutoCompletedFallbackJob> logger)
    {
        _trips = trips;
        _sender = sender;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = _clock.UtcNow.AddMinutes(-30);
        var candidateIds = await _trips.QueryNoTracking()
            .Where(trip => trip.Status == TripStatus.IN_PROGRESS
                && trip.EstimatedArrivalTime <= cutoff)
            .OrderBy(trip => trip.EstimatedArrivalTime)
            .Select(trip => trip.Id)
            .Take(500)
            .ToArrayAsync(cancellationToken);

        foreach (var tripId in candidateIds)
        {
            try
            {
                await _sender.Send(
                    new CompleteTripCommand(tripId, ActorUserId: null, IsAutomatic: true),
                    cancellationToken);
            }
            catch (ConflictException)
            {
                _logger.LogDebug(
                    "Auto-completion skipped trip {TripId}; another terminal transition won the race.",
                    tripId);
            }
            catch (CodedNotFoundException)
            {
                _logger.LogDebug(
                    "Auto-completion skipped deleted trip {TripId}.",
                    tripId);
            }
        }
    }
}
