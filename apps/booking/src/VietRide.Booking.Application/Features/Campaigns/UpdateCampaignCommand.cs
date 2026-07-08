using MediatR;

namespace VietRide.Booking.Application.Features.Campaigns;

public sealed record UpdateCampaignCommand(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    bool IsActive,
    IReadOnlyList<Guid> VoucherIds) : IRequest<CampaignDto>;
