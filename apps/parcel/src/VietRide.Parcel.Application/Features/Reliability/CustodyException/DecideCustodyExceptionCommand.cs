using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyException;

public sealed record DecideCustodyExceptionCommand(
    Guid SubjectId,
    string SubjectType,
    Guid ReviewerUserId,
    Guid OperatorId,
    string ReviewerRole,
    string Decision,
    string? Note,
    Guid IdempotencyKey) : IRequest<ReportCustodyExceptionResponse>;
