using VietRide.Shared.Application.Cqrs;

namespace VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;

public sealed record GetOperatorBookingStatsQuery(
    Guid OperatorId,
    DateOnly? From,
    DateOnly? To,
    string GroupBy) : IQuery<GetOperatorBookingStatsResult>;
