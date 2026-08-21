using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class GetParcelClaimsQueryHandler
    : IRequestHandler<GetParcelClaimsQuery, IReadOnlyList<ParcelClaimResponse>>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;

    public GetParcelClaimsQueryHandler(IParcelRepository parcels, IParcelReliabilityRepository reliability)
    {
        _parcels = parcels;
        _reliability = reliability;
    }

    public async Task<IReadOnlyList<ParcelClaimResponse>> Handle(
        GetParcelClaimsQuery request,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcels.GetByIdAsync(request.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (parcel.SenderUserId != request.UserId
            && request.OperatorId != parcel.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Caller is not authorized to view parcel claims.");

        var claims = await _reliability.ListClaimsByParcelAsync(parcel.Id, cancellationToken);
        var result = new List<ParcelClaimResponse>(claims.Count);
        var operatorView = request.OperatorId == parcel.OperatorId && request.UserId != parcel.SenderUserId;
        foreach (var claim in claims)
        {
            result.Add(await ParcelClaimResponseMapper.MapAsync(
                claim,
                _reliability,
                cancellationToken,
                parcel,
                operatorView: operatorView));
        }

        return result;
    }
}
