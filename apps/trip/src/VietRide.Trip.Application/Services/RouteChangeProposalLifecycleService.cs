using System.Text.Json;
using VietRide.Shared.Application.Outbox;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Services;

public sealed class RouteChangeProposalLifecycleService : IRouteChangeProposalLifecycleService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IRouteChangeProposalRepository proposals;
    private readonly ITripAuditLogRepository auditLogs;
    private readonly IIntegrationEventOutbox outbox;

    public RouteChangeProposalLifecycleService(
        IRouteChangeProposalRepository proposals,
        ITripAuditLogRepository auditLogs,
        IIntegrationEventOutbox outbox)
    {
        this.proposals = proposals;
        this.auditLogs = auditLogs;
        this.outbox = outbox;
    }

    public async Task SupersedePendingAsync(
        Guid tripId,
        Guid? actorUserId,
        Guid? approvedProposalId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pending = await proposals.AcquirePendingByTripAsync(tripId, cancellationToken);
        foreach (var proposal in pending.Where(item => item.Id != approvedProposalId))
        {
            proposal.Supersede(
                actorUserId,
                approvedProposalId,
                RouteChangeProposalResolutionCode.RouteChangedDirectly,
                now);
            await RecordTerminalTransitionAsync(
                proposal,
                TripAuditAction.RouteChangeProposalSuperseded,
                RouteChangeProposalIntegrationEvent.Superseded,
                actorUserId,
                now,
                cancellationToken);
        }
    }

    public async Task ExpirePendingForSourceAsync(
        Guid sourceAlternativeRouteId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await proposals.AcquireSourceCoordinationLockAsync(sourceAlternativeRouteId, cancellationToken);
        var pending = await proposals.AcquirePendingBySourceAsync(sourceAlternativeRouteId, cancellationToken);
        foreach (var proposal in pending)
        {
            await ExpireAsync(
                proposal,
                RouteChangeProposalResolutionCode.SourceRouteChanged,
                now,
                cancellationToken);
        }
    }

    public async Task ExpirePendingForTripAsync(
        Guid tripId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pending = await proposals.AcquirePendingByTripAsync(tripId, cancellationToken);
        foreach (var proposal in pending)
        {
            await ExpireAsync(
                proposal,
                RouteChangeProposalResolutionCode.TripNoLongerEditable,
                now,
                cancellationToken);
        }
    }

    private async Task ExpireAsync(
        RouteChangeProposal proposal,
        string resolutionCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        proposal.Expire(resolutionCode, now);
        await RecordTerminalTransitionAsync(
            proposal,
            TripAuditAction.RouteChangeProposalExpired,
            RouteChangeProposalIntegrationEvent.Expired,
            null,
            now,
            cancellationToken);
    }

    private async Task RecordTerminalTransitionAsync(
        RouteChangeProposal proposal,
        string auditAction,
        string eventType,
        Guid? actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var metadata = JsonSerializer.Serialize(
            new
            {
                proposalId = proposal.Id,
                proposalType = proposal.Type.ToString(),
                status = proposal.Status.ToString(),
            },
            JsonOptions);
        await auditLogs.AddAsync(
            TripAuditLog.Create(
                Guid.NewGuid(),
                proposal.TripId,
                actorUserId,
                auditAction,
                metadata,
                now),
            cancellationToken);

        var integrationEvent = new RouteChangeProposalIntegrationEvent(
            eventType,
            proposal.Id,
            proposal.TripId,
            proposal.OperatorId,
            proposal.ProposedByUserId,
            actorUserId,
            proposal.Type.ToString(),
            proposal.Status.ToString(),
            proposal.SourceAlternativeRouteId,
            proposal.ApprovedAlternativeRouteId,
            proposal.IncidentId,
            proposal.Reason,
            proposal.RejectionReason,
            proposal.ResolutionCode,
            proposal.SupersededByProposalId,
            now);
        await outbox.EnqueueAsync(
            integrationEvent.EventId,
            integrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent, JsonOptions),
            cancellationToken);
    }
}
