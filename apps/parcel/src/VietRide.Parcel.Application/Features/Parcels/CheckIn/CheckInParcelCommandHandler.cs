using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.CheckIn;

public sealed class CheckInParcelCommandHandler
    : IRequestHandler<CheckInParcelCommand, CheckInParcelResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly ITripServiceClient _trips;
    private readonly IClock _clock;

    public CheckInParcelCommandHandler(
        IParcelRepository parcels,
        ITripServiceClient trips,
        IClock clock)
    {
        _parcels = parcels;
        _trips = trips;
        _clock = clock;
    }

    public async Task<CheckInParcelResponse> Handle(
        CheckInParcelCommand command,
        CancellationToken cancellationToken)
    {
        var authorization = await _trips.AuthorizeAssistantForTripAsync(
            command.TripId,
            command.AssistantUserId,
            command.OperatorId,
            cancellationToken);
        if (authorization.Kind is TripCrewAuthorizationOutcomeKind.Denied
            or TripCrewAuthorizationOutcomeKind.TripNotFound)
            throw new ForbiddenException("FORBIDDEN", "Only the assigned assistant can check in this parcel.");
        if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                authorization.ErrorMessage ?? "Trip assignment verification failed.");

        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null
            || parcel.OperatorId != command.OperatorId
            || parcel.TripId != command.TripId
            || !string.Equals(parcel.ParcelCode, command.ParcelCode, StringComparison.Ordinal))
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found for this trip.");
        if (parcel.Status != ParcelStatus.RESERVED)
            throw new CodedConflictException("INVALID_STATUS", $"Parcel is in status '{parcel.Status}'.");

        var now = _clock.UtcNow;
        if (!parcel.LatestCheckInAt.HasValue || now >= parcel.LatestCheckInAt.Value)
            throw new CodedConflictException("PARCEL_CHECK_IN_CLOSED", "Parcel check-in deadline has passed.");

        var snapshot = await _parcels.TryCheckInAsync(
            parcel.Id,
            command.TripId,
            command.ParcelCode,
            command.AssistantUserId,
            now,
            cancellationToken)
            ?? throw new CodedConflictException("RACE_LOST", "Parcel status changed during check-in.");

        return new CheckInParcelResponse(
            snapshot.ParcelId,
            snapshot.ParcelCode,
            snapshot.Status.ToString(),
            now,
            parcel.LatestCheckInAt.Value);
    }
}
