using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.BookingTransfers.ConfirmPassengerTransfer;

public sealed class ConfirmPassengerTransferCommandHandler(
    IBookingTransferRepository transfers,
    ITripServiceClient tripServiceClient,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<ConfirmPassengerTransferCommand, ConfirmPassengerTransferResponse>
{
    public async Task<ConfirmPassengerTransferResponse> Handle(
        ConfirmPassengerTransferCommand request,
        CancellationToken cancellationToken)
    {
        var trip = await tripServiceClient.GetTripSnapshotAsync(
            request.NewTripId,
            cancellationToken);

        if (trip is null)
        {
            throw TransferNotFound();
        }

        if (trip.DriverUserId != request.CallerUserId
            && trip.AssistantUserId != request.CallerUserId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Caller is not assigned to the replacement trip.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var transfer = await transfers.GetActiveForConfirmationAsync(
                request.PassengerId,
                request.NewTripId,
                trip.OperatorId,
                cancellationToken);

            if (transfer is null)
            {
                throw TransferNotFound();
            }

            if (transfer.NewSeatNumber is null)
            {
                throw new ConflictException(
                    "BOOKING_TRANSFER_SEAT_PENDING",
                    "The replacement seat has not been assigned.");
            }

            if (transfer.ConfirmationStatus == BookingTransferConfirmationStatus.PENDING_CONFIRM)
            {
                transfer.Confirm(request.CallerUserId, clock.UtcNow.ToUniversalTime());
                transfers.Update(transfer);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return MapResponse(transfer);
        }, cancellationToken);
    }

    private static ConfirmPassengerTransferResponse MapResponse(
        Domain.Entities.BookingTransfer transfer)
        => new(
            transfer.Id,
            transfer.PassengerId,
            transfer.NewTripId,
            transfer.ConfirmationStatus.ToString(),
            transfer.ConfirmedAt
                ?? throw new InvalidOperationException("Confirmed transfer timestamp is missing."),
            transfer.ConfirmedByUserId
                ?? throw new InvalidOperationException("Confirmed transfer actor is missing."));

    private static CodedNotFoundException TransferNotFound()
        => new(
            "BOOKING_TRANSFER_NOT_FOUND",
            "Active BookingTransfer was not found for this passenger and replacement trip.");
}
