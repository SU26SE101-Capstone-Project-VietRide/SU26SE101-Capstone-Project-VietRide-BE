using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed record HandleTripDisruptedCommand(
    Guid TripId,
    bool HasSubstitution,
    decimal TraveledRatio) : IRequest<int>;
