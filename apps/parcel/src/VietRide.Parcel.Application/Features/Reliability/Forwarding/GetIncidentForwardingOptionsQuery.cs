using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Forwarding;

public sealed record GetIncidentForwardingOptionsQuery(
    Guid IncidentId,
    Guid OperatorId,
    int Limit = 20) : IRequest<IReadOnlyList<IncidentForwardingOptionResponse>>;
