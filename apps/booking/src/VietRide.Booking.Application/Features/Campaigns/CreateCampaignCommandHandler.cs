using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Application.Features.Campaigns;

public sealed class CreateCampaignCommandHandler : IRequestHandler<CreateCampaignCommand, CampaignDto>
{
    private readonly ICampaignRepository _campaigns;

    public CreateCampaignCommandHandler(ICampaignRepository campaigns)
    {
        _campaigns = campaigns;
    }

    public async Task<CampaignDto> Handle(CreateCampaignCommand request, CancellationToken cancellationToken)
    {
        var campaign = Campaign.Create(
            request.Name,
            request.Description,
            request.OwnerOperatorId,
            request.ValidFrom,
            request.ValidUntil,
            request.CreatedByUserId);
        await _campaigns.AddAsync(campaign, cancellationToken);
        await _campaigns.ReplaceVouchersAsync(campaign.Id, request.VoucherIds, cancellationToken);
        return ToDto(campaign);
    }

    private static CampaignDto ToDto(Campaign campaign)
        => new(campaign.Id, campaign.Name, campaign.Description, campaign.OwnerOperatorId, campaign.IsActive, campaign.ValidFrom, campaign.ValidUntil, campaign.CreatedAt);
}
