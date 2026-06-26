using MediatR;

namespace VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;

public sealed record GetAdminBookingStatsAggregateQuery(
    DateOnly? From,
    DateOnly? To,
    string GroupBy) : IRequest<GetAdminBookingStatsAggregateResult>;
