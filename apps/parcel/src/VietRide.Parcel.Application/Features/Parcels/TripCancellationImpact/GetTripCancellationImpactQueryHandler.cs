using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;

namespace VietRide.Parcel.Application.Features.Parcels.TripCancellationImpact;

public sealed class GetTripCancellationImpactQueryHandler(IParcelRepository parcels)
    : IRequestHandler<GetTripCancellationImpactQuery, TripCancellationImpactResponse>
{
    public async Task<TripCancellationImpactResponse> Handle(
        GetTripCancellationImpactQuery request,
        CancellationToken cancellationToken)
    {
        var candidates = await parcels.GetTripCancellationCandidatesAsync(
            request.TripId,
            request.OperatorId,
            cancellationToken);

        var impacts = candidates
            .Select(candidate => (Candidate: candidate, Classification:
                ParcelTripCancellationClassifier.Classify(candidate)))
            .Where(item => item.Classification.Disposition
                is not ParcelTripCancellationDisposition.None)
            .Select(item => new TripCancellationImpactResponse.AffectedParcel(
                item.Candidate.ParcelId,
                item.Candidate.Status.ToString(),
                item.Classification.RefundAmountVnd))
            .ToArray();

        return new TripCancellationImpactResponse(
            request.TripId,
            impacts);
    }
}
