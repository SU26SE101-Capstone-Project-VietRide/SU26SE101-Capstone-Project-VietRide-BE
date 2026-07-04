using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed record HandleTripStartedCommand(Guid TripId) : IRequest<int>;
