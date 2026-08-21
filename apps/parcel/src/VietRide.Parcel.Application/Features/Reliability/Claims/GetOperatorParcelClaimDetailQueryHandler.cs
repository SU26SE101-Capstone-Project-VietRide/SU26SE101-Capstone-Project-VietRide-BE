using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Application.Services;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class GetOperatorParcelClaimDetailQueryHandler
    : IRequestHandler<GetOperatorParcelClaimDetailQuery, OperatorParcelClaimDetailResponse>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelRepository _parcels;
    private readonly ITripServiceClient _trips;
    private readonly IIdentityServiceClient _identity;
    private readonly IClock _clock;

    public GetOperatorParcelClaimDetailQueryHandler(
        IParcelReliabilityRepository reliability,
        IParcelRepository parcels,
        ITripServiceClient trips,
        IIdentityServiceClient identity,
        IClock clock)
    {
        _reliability = reliability;
        _parcels = parcels;
        _trips = trips;
        _identity = identity;
        _clock = clock;
    }

    public async Task<OperatorParcelClaimDetailResponse> Handle(
        GetOperatorParcelClaimDetailQuery request,
        CancellationToken cancellationToken)
    {
        var claim = await _reliability.GetClaimByIdAsync(request.ClaimId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_CLAIM_NOT_FOUND", "Claim was not found.");
        if (claim.OperatorId != request.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Claim does not belong to this operator.");
        var parcel = await _parcels.GetByIdAsync(claim.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (parcel.OperatorId != request.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");
        var incident = await _reliability.GetIncidentAsync(claim.IncidentId, cancellationToken);
        var current = await _reliability.GetCurrentCustodyAsync(parcel.Id, cancellationToken);
        var now = _clock.UtcNow;
        var claimResponse = await ParcelClaimResponseMapper.MapAsync(
            claim,
            _reliability,
            cancellationToken,
            parcel,
            incident,
            operatorView: true,
            now: now);
        var tripOutcome = await _trips.GetTripSummariesAsync([parcel.TripId], cancellationToken);
        var tripSnapshot = tripOutcome.Kind == TripSummaryBatchOutcomeKind.Success
            ? tripOutcome.Summaries.FirstOrDefault()
            : null;
        var trip = ParcelReliabilityReadModelService.MapTrip(parcel, tripSnapshot);
        var userOutcome = await _identity.GetUsersAsync([claim.BeneficiaryUserId], cancellationToken);
        var users = userOutcome.Kind == IdentityUserBatchOutcomeKind.Success
            ? userOutcome.Users.ToDictionary(user => user.Id)
            : new Dictionary<Guid, IdentityUserSummary>();
        return new OperatorParcelClaimDetailResponse(
            claimResponse,
            ListParcelIncidentsQueryHandler.MapParcel(parcel),
            ParcelReliabilityReadModelService.MapIncident(incident, now),
            ParcelReliabilityReadModelService.MapCustody(current),
            trip,
            ParcelReliabilityReadModelService.MapDropoff(parcel, trip),
            ListParcelIncidentsQueryHandler.MapUser(claim.BeneficiaryUserId, users, "BENEFICIARY"),
            ListOperatorParcelClaimsQueryHandler.FundingStatus(claim.Status),
            ListOperatorParcelClaimsQueryHandler.ClaimActions(claim.Status));
    }
}
