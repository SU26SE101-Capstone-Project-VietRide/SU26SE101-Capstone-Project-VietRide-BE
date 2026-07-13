using MediatR;

namespace VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;

public sealed record GetOperatorBookingDetailQuery(Guid BookingId, Guid OperatorId) : IRequest<OperatorBookingDetailDto>;
