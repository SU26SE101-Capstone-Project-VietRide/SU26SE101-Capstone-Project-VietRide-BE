using MediatR;

namespace VietRide.Payment.Application.Features.Internal.Payments.LookupRedirectSessions;

public sealed record LookupRedirectSessionsQuery(
    Guid UserId,
    IReadOnlyList<LookupRedirectSessionsQuery.Reference> References)
    : IRequest<IReadOnlyList<LookupRedirectSessionsResult>>
{
    public sealed record Reference(string ReferenceType, Guid ReferenceId);
}
