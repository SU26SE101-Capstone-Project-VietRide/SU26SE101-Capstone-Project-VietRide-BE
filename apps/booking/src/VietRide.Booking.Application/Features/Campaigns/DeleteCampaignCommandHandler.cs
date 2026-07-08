using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Campaigns;

public sealed class DeleteCampaignCommandHandler : IRequestHandler<DeleteCampaignCommand, Unit>
{
    private readonly ICampaignRepository _campaigns;

    public DeleteCampaignCommandHandler(ICampaignRepository campaigns)
    {
        _campaigns = campaigns;
    }

    public async Task<Unit> Handle(DeleteCampaignCommand request, CancellationToken cancellationToken)
    {
        var campaign = await _campaigns.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new CodedNotFoundException("CAMPAIGN_NOT_FOUND", "Campaign not found.");
        campaign.SoftDelete(request.DeletedAt);
        _campaigns.Update(campaign);
        return Unit.Value;
    }
}
