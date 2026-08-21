using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Services;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;

public sealed class GetUnidentifiedPackageQueryHandler
    : IRequestHandler<GetUnidentifiedPackageQuery, UnidentifiedPackageResponse>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelRepository _parcels;
    private readonly ITripServiceClient _trips;

    public GetUnidentifiedPackageQueryHandler(
        IParcelReliabilityRepository reliability,
        IParcelRepository parcels,
        ITripServiceClient trips)
    {
        _reliability = reliability;
        _parcels = parcels;
        _trips = trips;
    }

    public async Task<UnidentifiedPackageResponse> Handle(
        GetUnidentifiedPackageQuery request,
        CancellationToken cancellationToken)
    {
        var package = await _reliability.GetUnidentifiedPackageAsync(request.PackageId, cancellationToken)
            ?? throw new CodedNotFoundException("UNIDENTIFIED_PACKAGE_NOT_FOUND", "Unidentified package was not found.");
        if (package.OperatorId != request.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Package does not belong to this operator.");
        VietRide.Parcel.Domain.Entities.Parcel? matched = null;
        if (package.MatchedParcelId.HasValue)
            matched = await _parcels.GetByIdAsync(package.MatchedParcelId.Value, cancellationToken);
        Reliability.ReadModels.ReliabilityTripResponse? trip = null;
        if (package.TripId.HasValue)
        {
            var outcome = await _trips.GetTripSummariesAsync([package.TripId.Value], cancellationToken);
            if (outcome.Kind == TripSummaryBatchOutcomeKind.Success && outcome.Summaries.FirstOrDefault() is { } snapshot)
                trip = ParcelReliabilityReadModelService.MapTrip(snapshot);
        }
        return UnidentifiedPackageReadModelMapper.Map(package, trip, matched);
    }
}
