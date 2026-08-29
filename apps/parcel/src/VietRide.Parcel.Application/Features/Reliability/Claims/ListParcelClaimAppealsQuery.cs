using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record ListParcelClaimAppealsQuery(
    Guid OperatorId,
    string? Status,
    int Page,
    int PageSize) : IRequest<PagedResult<ParcelClaimAppealResponse>>;
