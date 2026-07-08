using MediatR;

namespace VietRide.Booking.Application.Features.Campaigns;

public sealed record SetCampaignActiveCommand(Guid Id, bool IsActive) : IRequest<CampaignDto>;
