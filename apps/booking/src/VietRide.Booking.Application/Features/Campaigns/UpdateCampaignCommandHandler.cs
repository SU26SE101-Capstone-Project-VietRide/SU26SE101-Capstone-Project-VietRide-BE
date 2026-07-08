using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Campaigns;

public sealed class UpdateCampaignCommandHandler : IRequestHandler<UpdateCampaignCommand, CampaignDto>
{
    private readonly ICampaignRepository _campaigns;

    public UpdateCampaignCommandHandler(ICampaignRepository campaigns)
    {
        _campaigns = campaigns;
    }

    public async Task<CampaignDto> Handle(UpdateCampaignCommand request, CancellationToken cancellationToken)
    {
        var campaign = await _campaigns.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new CodedNotFoundException("CAMPAIGN_NOT_FOUND", "Campaign not found.");
        campaign.Update(request.Name, request.Description, request.ValidFrom, request.ValidUntil, request.IsActive);
        _campaigns.Update(campaign);
        await _campaigns.ReplaceVouchersAsync(campaign.Id, request.VoucherIds, cancellationToken);
        return new CampaignDto(campaign.Id, campaign.Name, campaign.Description, campaign.OwnerOperatorId, campaign.IsActive, campaign.ValidFrom, campaign.ValidUntil, campaign.CreatedAt);
    }
}
