using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Booking.Application.Features.BookingTransfers.EscalatePendingTransfers;

[SkipTransaction]
public sealed record EscalatePendingBookingTransfersCommand : IRequest<int>;
