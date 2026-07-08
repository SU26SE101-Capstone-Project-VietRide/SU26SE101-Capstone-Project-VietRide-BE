using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Campaigns;

public sealed class SetCampaignActiveCommandHandler : IRequestHandler<SetCampaignActiveCommand, CampaignDto>
{
    private readonly ICampaignRepository _campaigns;

    public SetCampaignActiveCommandHandler(ICampaignRepository campaigns)
    {
        _campaigns = campaigns;
    }

    public async Task<CampaignDto> Handle(SetCampaignActiveCommand request, CancellationToken cancellationToken)
    {
        var campaign = await _campaigns.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new CodedNotFoundException("CAMPAIGN_NOT_FOUND", "Campaign not found.");

        if (request.IsActive)
        {
            campaign.Activate();
        }
        else
        {
            campaign.Deactivate();
        }

        _campaigns.Update(campaign);

        return new CampaignDto(
            campaign.Id,
            campaign.Name,
            campaign.Description,
            campaign.OwnerOperatorId,
            campaign.IsActive,
            campaign.ValidFrom,
            campaign.ValidUntil,
            campaign.CreatedAt);
    }
}
