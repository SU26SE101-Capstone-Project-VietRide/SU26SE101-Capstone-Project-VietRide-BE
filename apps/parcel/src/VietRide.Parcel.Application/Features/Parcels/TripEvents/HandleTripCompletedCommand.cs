using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed record HandleTripCompletedCommand(Guid TripId) : IRequest<int>;
