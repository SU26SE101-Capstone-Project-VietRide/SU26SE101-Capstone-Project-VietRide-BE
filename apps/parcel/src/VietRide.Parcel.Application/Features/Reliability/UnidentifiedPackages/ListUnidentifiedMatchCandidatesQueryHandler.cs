using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;

public sealed class ListUnidentifiedMatchCandidatesQueryHandler
    : IRequestHandler<ListUnidentifiedMatchCandidatesQuery, IReadOnlyList<UnidentifiedPackageMatchCandidateResponse>>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly ITripServiceClient _trips;

    public ListUnidentifiedMatchCandidatesQueryHandler(
        IParcelReliabilityRepository reliability,
        ITripServiceClient trips)
    {
        _reliability = reliability;
        _trips = trips;
    }

    public async Task<IReadOnlyList<UnidentifiedPackageMatchCandidateResponse>> Handle(
        ListUnidentifiedMatchCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Limit is < 1 or > 50)
            throw new CodedValidationException("VALIDATION_ERROR", "limit must be between 1 and 50.");
        var package = await _reliability.GetUnidentifiedPackageAsync(request.PackageId, cancellationToken)
            ?? throw new CodedNotFoundException("UNIDENTIFIED_PACKAGE_NOT_FOUND", "Unidentified package was not found.");
        if (package.OperatorId != request.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Package does not belong to this operator.");
        if (package.Status != UnidentifiedParcelPackageStatus.UNIDENTIFIED)
            return [];
        var candidates = await _reliability.ListUnidentifiedMatchCandidatesAsync(
            request.OperatorId,
            package.TripId,
            package.ObservedWeightKg,
            request.Limit,
            cancellationToken);
        var tripOutcome = await _trips.GetTripSummariesAsync(
            candidates.Select(parcel => parcel.TripId).Distinct().ToArray(),
            cancellationToken);
        var trips = tripOutcome.Kind == TripSummaryBatchOutcomeKind.Success
            ? tripOutcome.Summaries.ToDictionary(trip => trip.TripId)
            : new Dictionary<Guid, TripSummarySnapshot>();
        return candidates.Select(parcel =>
        {
            trips.TryGetValue(parcel.TripId, out var tripSnapshot);
            var trip = ParcelReliabilityReadModelService.MapTrip(parcel, tripSnapshot);
            var reasons = new List<string>();
            if (package.TripId == parcel.TripId)
                reasons.Add("SAME_TRIP_MANIFEST");
            if (package.ObservedWeightKg.HasValue)
                reasons.Add("WEIGHT_WITHIN_TOLERANCE");
            if (!string.IsNullOrWhiteSpace(package.Description)
                && !string.IsNullOrWhiteSpace(parcel.Description)
                && (parcel.Description.Contains(package.Description, StringComparison.OrdinalIgnoreCase)
                    || package.Description.Contains(parcel.Description, StringComparison.OrdinalIgnoreCase)))
                reasons.Add("DESCRIPTION_SIMILAR");
            return new UnidentifiedPackageMatchCandidateResponse(
                parcel.Id,
                parcel.ParcelCode,
                trip,
                parcel.PhotoUrl,
                parcel.Description,
                parcel.ActualWeightKg ?? parcel.EstimatedWeightKg,
                ParcelReliabilityReadModelService.MapDropoff(parcel, trip),
                reasons);
        }).ToArray();
    }
}
