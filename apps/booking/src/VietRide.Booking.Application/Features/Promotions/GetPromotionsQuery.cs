using MediatR;

namespace VietRide.Booking.Application.Features.Promotions;

public sealed record GetPromotionsQuery(string Service) : IRequest<IReadOnlyList<PromotionItem>>;

public sealed record PromotionItem(
    Guid VoucherId,
    string Code,
    string Name,
    string Type,
    long Value,
    IReadOnlyList<string> ApplicableServices,
    DateTimeOffset ValidUntil);
