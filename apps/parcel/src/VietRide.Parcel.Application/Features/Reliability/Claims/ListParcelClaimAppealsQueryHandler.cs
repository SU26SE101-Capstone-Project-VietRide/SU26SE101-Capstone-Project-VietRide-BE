using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class ListParcelClaimAppealsQueryHandler
    : IRequestHandler<ListParcelClaimAppealsQuery, PagedResult<ParcelClaimAppealResponse>>
{
    private readonly IParcelReliabilityRepository _reliability;

    public ListParcelClaimAppealsQueryHandler(IParcelReliabilityRepository reliability)
    {
        _reliability = reliability;
    }

    public async Task<PagedResult<ParcelClaimAppealResponse>> Handle(
        ListParcelClaimAppealsQuery query,
        CancellationToken cancellationToken)
    {
        ParcelClaimAppealStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<ParcelClaimAppealStatus>(query.Status, true, out var parsed))
                throw new CodedValidationException("VALIDATION_ERROR", "Invalid claim appeal status.");
            status = parsed;
        }
        var result = await _reliability.SearchClaimAppealsByOperatorAsync(
            query.OperatorId,
            status,
            query.Page,
            query.PageSize,
            cancellationToken);
        return PagedResult<ParcelClaimAppealResponse>.Create(
            result.Items.Select(item => ParcelClaimAppealResponseMapper.Map(item, operatorView: true)).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalItems);
    }
}
