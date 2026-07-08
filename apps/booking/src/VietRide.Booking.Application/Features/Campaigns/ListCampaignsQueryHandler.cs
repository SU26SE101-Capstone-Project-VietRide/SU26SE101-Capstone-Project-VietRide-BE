using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;

namespace VietRide.Booking.Application.Features.Campaigns;

public sealed class ListCampaignsQueryHandler : IRequestHandler<ListCampaignsQuery, IReadOnlyList<CampaignDto>>
{
    private readonly ICampaignRepository _campaigns;

    public ListCampaignsQueryHandler(ICampaignRepository campaigns)
    {
        _campaigns = campaigns;
    }

    public async Task<IReadOnlyList<CampaignDto>> Handle(ListCampaignsQuery request, CancellationToken cancellationToken)
        => (await _campaigns.ListAsync(cancellationToken))
            .Select(x => new CampaignDto(x.Id, x.Name, x.Description, x.OwnerOperatorId, x.IsActive, x.ValidFrom, x.ValidUntil, x.CreatedAt))
            .ToArray();
}
