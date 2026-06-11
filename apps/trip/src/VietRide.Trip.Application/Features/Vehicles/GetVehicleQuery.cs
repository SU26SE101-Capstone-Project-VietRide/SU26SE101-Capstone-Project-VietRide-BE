using MediatR;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed record GetVehicleQuery(Guid OperatorId, Guid VehicleId) : IRequest<VehicleDto>;
