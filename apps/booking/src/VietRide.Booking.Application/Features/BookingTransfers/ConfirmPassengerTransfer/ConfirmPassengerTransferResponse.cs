namespace VietRide.Booking.Application.Features.BookingTransfers.ConfirmPassengerTransfer;

public sealed record ConfirmPassengerTransferResponse(
    Guid BookingTransferId,
    Guid PassengerId,
    Guid NewTripId,
    string ConfirmationStatus,
    DateTimeOffset ConfirmedAt,
    Guid ConfirmedByUserId);
