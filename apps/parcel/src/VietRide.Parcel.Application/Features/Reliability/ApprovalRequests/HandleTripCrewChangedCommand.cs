using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.ApprovalRequests;

public sealed record HandleTripCrewChangedCommand(
    Guid TripId,
    Guid OperatorId,
    Guid? OldDriverUserId,
    Guid? DriverUserId) : IRequest<int>;
