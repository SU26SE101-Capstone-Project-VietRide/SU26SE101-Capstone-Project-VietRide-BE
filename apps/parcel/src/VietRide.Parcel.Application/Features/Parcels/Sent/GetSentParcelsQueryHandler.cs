using MediatR;
using VietRide.Parcel.Application.Features.History;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.Sent;

public sealed class GetSentParcelsQueryHandler
    : IRequestHandler<GetSentParcelsQuery, PagedResult<SentParcelHistoryItemDto>>
{
    private readonly SentParcelHistoryReader _reader;

    public GetSentParcelsQueryHandler(SentParcelHistoryReader reader)
    {
        _reader = reader;
    }

    public Task<PagedResult<SentParcelHistoryItemDto>> Handle(
        GetSentParcelsQuery request,
        CancellationToken cancellationToken)
        => _reader.ReadAsync(
            request.UserId,
            request.Status,
            request.From,
            request.To,
            request.Page,
            request.PageSize,
            cancellationToken);
}
