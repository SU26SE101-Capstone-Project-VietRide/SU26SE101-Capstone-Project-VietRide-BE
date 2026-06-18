using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.CreateRoundTripBooking;

/// <summary>
/// Command for POST /v1/bookings/round-trip — creates two independent bookings
/// linked by a display-only bookingGroupId.
/// </summary>
public sealed record CreateRoundTripBookingCommand : IRequest<CreateRoundTripBookingResult>
{
    public CreateRoundTripBookingCommand(
        Guid passengerUserId,
        string idempotencyKey,
        RoundTripBookingLegCommand outbound,
        RoundTripBookingLegCommand @return,
        string? voucherCode,
        string paymentMethod)
    {
        PassengerUserId = passengerUserId;
        IdempotencyKey = idempotencyKey;
        Outbound = outbound;
        Return = @return;
        VoucherCode = voucherCode;
        PaymentMethod = paymentMethod;
    }

    public Guid PassengerUserId { get; init; }

    public string IdempotencyKey { get; init; }

    public RoundTripBookingLegCommand Outbound { get; init; }

    public RoundTripBookingLegCommand Return { get; init; }

    public string? VoucherCode { get; init; }

    public string PaymentMethod { get; init; }

    public sealed record RoundTripBookingLegCommand(
        Guid TripId,
        Guid? PickupStationId,
        Guid? PickupStopId,
        Guid? DropoffStationId,
        Guid? DropoffStopId,
        IReadOnlyList<RoundTripSeatRequest> Seats);

    /// <summary>
    /// Per-seat booking request. PII is validated but NOT persisted.
    /// </summary>
    public sealed record RoundTripSeatRequest(
        string SeatNumber,
        string FullName,
        string PhoneNumber,
        string IdNumber);
}
