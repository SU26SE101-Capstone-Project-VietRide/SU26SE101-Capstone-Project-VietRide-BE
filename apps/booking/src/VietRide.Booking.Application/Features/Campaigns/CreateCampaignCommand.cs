using MediatR;

namespace VietRide.Booking.Application.Features.Campaigns;

public sealed record CreateCampaignCommand(
    string Name,
    string? Description,
    Guid? OwnerOperatorId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    IReadOnlyList<Guid> VoucherIds,
    Guid CreatedByUserId) : IRequest<CampaignDto>;
