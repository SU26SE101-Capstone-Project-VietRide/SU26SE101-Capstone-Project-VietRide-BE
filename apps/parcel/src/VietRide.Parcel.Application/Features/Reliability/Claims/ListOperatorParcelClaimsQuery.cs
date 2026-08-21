using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record ListOperatorParcelClaimsQuery(
    Guid OperatorId,
    string? Status,
    string? Search,
    string? SlaState,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize) : IRequest<PagedResult<OperatorParcelClaimListItem>>;
