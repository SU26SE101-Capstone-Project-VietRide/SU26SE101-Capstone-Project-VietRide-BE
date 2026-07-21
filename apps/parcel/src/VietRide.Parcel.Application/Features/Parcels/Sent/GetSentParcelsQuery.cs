using MediatR;
using VietRide.Parcel.Application.Features.History;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.Sent;

public sealed record GetSentParcelsQuery(
    Guid UserId,
    string? Status,
    string? From,
    string? To,
    int Page,
    int PageSize) : IRequest<PagedResult<SentParcelHistoryItemDto>>;
