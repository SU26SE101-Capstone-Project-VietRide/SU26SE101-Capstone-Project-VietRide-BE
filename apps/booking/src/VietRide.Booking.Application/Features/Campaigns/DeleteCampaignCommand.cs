using MediatR;

namespace VietRide.Booking.Application.Features.Campaigns;

public sealed record DeleteCampaignCommand(Guid Id, DateTimeOffset DeletedAt) : IRequest<Unit>;
