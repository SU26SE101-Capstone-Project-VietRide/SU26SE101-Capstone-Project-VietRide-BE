using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class GetParcelClaimAppealQueryHandler
    : IRequestHandler<GetParcelClaimAppealQuery, ParcelClaimAppealResponse>
{
    private readonly IParcelReliabilityRepository _reliability;

    public GetParcelClaimAppealQueryHandler(IParcelReliabilityRepository reliability)
    {
        _reliability = reliability;
    }

    public async Task<ParcelClaimAppealResponse> Handle(
        GetParcelClaimAppealQuery query,
        CancellationToken cancellationToken)
    {
        var appeal = await _reliability.GetClaimAppealByIdAsync(query.AppealId, cancellationToken);
        if (appeal is null || appeal.OperatorId != query.OperatorId)
            throw new CodedNotFoundException("PARCEL_CLAIM_APPEAL_NOT_FOUND", "Claim appeal was not found.");
        var acceptedEvidenceIds = (await _reliability.ListClaimAppealDecisionEvidenceAsync(
            appeal.Id,
            cancellationToken)).Select(link => link.EvidenceId).ToArray();
        return ParcelClaimAppealResponseMapper.Map(appeal, operatorView: true, acceptedEvidenceIds);
    }
}
