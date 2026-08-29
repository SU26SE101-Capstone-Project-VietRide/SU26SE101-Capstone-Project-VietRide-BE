using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyException;

public sealed record GetCustodyExceptionRequestQuery(
    Guid ParcelId,
    Guid ReviewerUserId,
    Guid OperatorId,
    string ReviewerRole) : IRequest<ReportCustodyExceptionResponse>;
