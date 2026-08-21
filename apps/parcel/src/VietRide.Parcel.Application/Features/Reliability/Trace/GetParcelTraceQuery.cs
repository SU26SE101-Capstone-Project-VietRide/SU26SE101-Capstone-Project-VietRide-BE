using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Trace;

public sealed record GetParcelTraceQuery(
    Guid ParcelId,
    Guid? UserId,
    Guid? OperatorId,
    string? Cursor = null,
    int Limit = 50) : IRequest<ParcelTraceResponse>;
