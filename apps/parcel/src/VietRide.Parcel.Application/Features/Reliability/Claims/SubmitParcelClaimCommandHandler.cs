using System.Text.Json;
using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Reliability.CustodyException;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class SubmitParcelClaimCommandHandler
    : IRequestHandler<SubmitParcelClaimCommand, ParcelClaimResponse>
{
    public const int DefaultPolicyVersion = 1;
    public const int DefaultCompensationRatePercent = ParcelCompensationPolicy.DefaultRatePercent;
    public const long DefaultPolicyCapVnd = ParcelCompensationPolicy.DefaultMaximumCompensationVnd;
    public const int DefaultNoProofFallbackMultiplier = ParcelCompensationPolicy.DefaultNoProofFallbackMultiplier;

    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelCustodyExceptionRequestRepository _custodyExceptionRequests;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public SubmitParcelClaimCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IParcelCustodyExceptionRequestRepository custodyExceptionRequests,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _parcels = parcels;
        _reliability = reliability;
        _custodyExceptionRequests = custodyExceptionRequests;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ParcelClaimResponse> Handle(
        SubmitParcelClaimCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (parcel.SenderUserId != command.SenderUserId)
            throw new ForbiddenException("FORBIDDEN", "Only the sender can submit a parcel claim.");

        var incident = (await _reliability.ListIncidentsByParcelAsync(parcel.Id, cancellationToken))
            .FirstOrDefault(x => x.Status == ParcelIncidentStatus.LOST_CONFIRMED);
        if (incident is null)
            throw new CodedConflictException(
                "PARCEL_CLAIM_WINDOW_NOT_OPEN",
                "A claim is available only after an incident is confirmed lost.");
        await CustodyExceptionApprovalGuard.EnsureNotPendingAsync(
            _custodyExceptionRequests,
            incident.Id,
            cancellationToken);
        var claimWindowDays = parcel.ClaimWindowDaysSnapshot > 0
            ? parcel.ClaimWindowDaysSnapshot
            : ParcelCompensationPolicy.DefaultClaimWindowDays;
        if (incident.ResolvedAt.HasValue
            && _clock.UtcNow > incident.ResolvedAt.Value.AddDays(claimWindowDays))
            throw new CodedConflictException(
                "PARCEL_INCIDENT_CLAIM_WINDOW_EXPIRED",
                "The parcel claim window has expired.");

        var existing = await _reliability.GetClaimByIncidentAsync(incident.Id, cancellationToken);
        if (existing is not null)
            throw new CodedConflictException("PARCEL_CLAIM_ALREADY_EXISTS", "A claim already exists for this incident.");

        var claim = ParcelClaim.Submit(
            parcel.Id,
            incident.Id,
            parcel.OperatorId,
            parcel.SenderUserId,
            parcel.DeclaredValueVnd,
            parcel.CompensationPolicyVersionSnapshot > 0
                ? parcel.CompensationPolicyVersionSnapshot
                : DefaultPolicyVersion,
            parcel.CompensationRatePercentSnapshot > 0
                ? parcel.CompensationRatePercentSnapshot
                : DefaultCompensationRatePercent,
            parcel.CompensationPolicyCapVndSnapshot > 0
                ? parcel.CompensationPolicyCapVndSnapshot
                : DefaultPolicyCapVnd,
            parcel.NoProofFallbackMultiplierSnapshot > 0
                ? parcel.NoProofFallbackMultiplierSnapshot
                : DefaultNoProofFallbackMultiplier);
        await _reliability.AddClaimAsync(claim, cancellationToken);

        var now = _clock.UtcNow;
        var inheritedEvidence = DeserializeIncidentEvidence(incident.EvidenceJson)
            .Select(reference => ParcelClaimEvidence.Create(
                claim.Id,
                ParcelClaimEvidence.IncidentPhotoEvidenceType,
                reference,
                "Inherited from the incident report.",
                incident.ReporterId ?? command.SenderUserId))
            .ToArray();
        foreach (var evidence in inheritedEvidence)
            await _reliability.AddClaimEvidenceAsync(evidence, cancellationToken);

        var submittedEventId = Guid.NewGuid();
        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            submittedEventId,
            ParcelOutboxEvents.ParcelClaimSubmitted,
            new
            {
                eventId = submittedEventId,
                occurredAt = now,
                claimId = claim.Id,
                parcelId = parcel.Id,
                incidentId = incident.Id,
                operatorId = parcel.OperatorId,
                beneficiaryUserId = parcel.SenderUserId,
                policyVersion = claim.PolicyVersion,
            },
            cancellationToken);

        var response = await ParcelClaimResponseMapper.MapAsync(
            claim,
            _reliability,
            cancellationToken,
            parcel,
            incident,
            operatorView: false,
            now: now);
        if (inheritedEvidence.Length == 0)
            return response;

        // The unit of work commits after this handler returns. Include inherited rows in the
        // immediate response because a database query cannot see Added entities before commit.
        return response with
        {
            Evidence =
            [
                .. response.Evidence,
                .. inheritedEvidence.Select(evidence => new ParcelClaimEvidenceResponse(
                    evidence.Id,
                    evidence.EvidenceType,
                    evidence.Reference,
                    evidence.Note,
                    evidence.UploadedByUserId,
                    now)),
            ],
        };
    }

    private static IReadOnlyList<string> DeserializeIncidentEvidence(string? evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
            return [];

        try
        {
            return (JsonSerializer.Deserialize<string[]>(evidenceJson) ?? [])
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Select(reference => reference.Trim())
                .Where(reference => reference.Length <= ParcelClaimEvidence.MaximumReferenceLength)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (JsonException)
        {
            // A malformed historical incident must not prevent the sender from submitting a claim.
            return [];
        }
    }
}
