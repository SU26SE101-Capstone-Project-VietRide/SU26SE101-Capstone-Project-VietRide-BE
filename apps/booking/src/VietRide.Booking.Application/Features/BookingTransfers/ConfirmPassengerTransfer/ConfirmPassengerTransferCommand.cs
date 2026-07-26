using MediatR;

namespace VietRide.Booking.Application.Features.BookingTransfers.ConfirmPassengerTransfer;

public sealed record ConfirmPassengerTransferCommand(
    Guid NewTripId,
    Guid PassengerId,
    Guid CallerUserId) : IRequest<ConfirmPassengerTransferResponse>;
