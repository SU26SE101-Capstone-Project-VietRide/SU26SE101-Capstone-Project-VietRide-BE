using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed record HandleTripCancelledCommand(Guid TripId) : IRequest<int>;
