using VietRide.Payment.Application.Features.Internal.Payments.LookupRedirectSessions;

namespace VietRide.Payment.Api.Controllers.Requests;

public sealed record LookupRedirectSessionsRequest(
    Guid UserId,
    IReadOnlyList<LookupRedirectSessionsRequest.Reference>? References)
{
    public LookupRedirectSessionsQuery ToQuery()
        => new(
            UserId,
            References?.Select(reference => new LookupRedirectSessionsQuery.Reference(
                reference.ReferenceType,
                reference.ReferenceId)).ToArray() ?? []);

    public sealed record Reference(string ReferenceType, Guid ReferenceId);
}
