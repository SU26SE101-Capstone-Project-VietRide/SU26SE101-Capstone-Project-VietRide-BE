using MediatR;

namespace VietRide.Booking.Application.Features.Campaigns;

public sealed record ListCampaignsQuery : IRequest<IReadOnlyList<CampaignDto>>;
