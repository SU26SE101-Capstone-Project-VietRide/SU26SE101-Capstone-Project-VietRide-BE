using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Domain.Helpers;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Features.Parcels.Create;

public sealed class CreateParcelCommandHandler
    : IRequestHandler<CreateParcelCommand, CreateParcelResponse>
{
    private readonly IIdentityServiceClient _identityClient;
    private readonly IBookingServiceClient _bookingClient;
    private readonly ITripServiceClient _tripClient;
    private readonly IPaymentServiceClient _paymentClient;
    private readonly IParcelRepository _parcelRepository;
    private readonly IParcelRouteFareRepository _fareRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;

    public CreateParcelCommandHandler(
        IIdentityServiceClient identityClient,
        IBookingServiceClient bookingClient,
        ITripServiceClient tripClient,
        IPaymentServiceClient paymentClient,
        IParcelRepository parcelRepository,
        IParcelRouteFareRepository fareRepository,
        IUnitOfWork unitOfWork,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository)
    {
        _identityClient = identityClient;
        _bookingClient = bookingClient;
        _tripClient = tripClient;
        _paymentClient = paymentClient;
        _parcelRepository = parcelRepository;
        _fareRepository = fareRepository;
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _statsRepository = statsRepository;
    }

    public async Task<CreateParcelResponse> Handle(
        CreateParcelCommand command,
        CancellationToken cancellationToken)
    {
        var userOutcome = await _identityClient.GetUserInfoAsync(command.SenderUserId, cancellationToken);
        switch (userOutcome.Kind)
        {
            case UserLookupOutcomeKind.TransportError:
                throw new ParcelDependencyUnavailableException(
                    "UPSTREAM_UNAVAILABLE",
                    userOutcome.ErrorMessage ?? "Identity service unavailable.");
            case UserLookupOutcomeKind.UserNotFound:
                throw new CodedNotFoundException(
                    "USER_NOT_FOUND",
                    $"User with id '{command.SenderUserId}' not found.");
            case UserLookupOutcomeKind.Forbidden:
                throw new ForbiddenException(
                    "USER_FORBIDDEN",
                    userOutcome.ErrorMessage ?? "User lookup not permitted.");
        }

        var user = userOutcome.UserInfo!;
        if (user.Role != "PASSENGER")
            throw new ForbiddenException("USER_NOT_PASSENGER", "Only passengers can create parcels.");
        if (user.Status != "ACTIVE")
            throw new ForbiddenException("USER_INACTIVE", "User account is not active.");

        if (command.BookingId.HasValue)
        {
            var bookingOutcome = await _bookingClient.GetBookingSnapshotAsync(command.BookingId.Value, cancellationToken);
            switch (bookingOutcome.Kind)
            {
                case BookingLookupOutcomeKind.TransportError:
                    throw new ParcelDependencyUnavailableException(
                        "BOOKING_SERVICE_UNAVAILABLE",
                        bookingOutcome.ErrorMessage ?? "Booking service unavailable.");
                case BookingLookupOutcomeKind.BookingNotFound:
                    throw new CodedNotFoundException(
                        "BOOKING_NOT_FOUND",
                        $"Booking with id '{command.BookingId}' not found.");
            }

            var booking = bookingOutcome.Snapshot!;
            if (booking.UserId != command.SenderUserId)
                throw new ForbiddenException(
                    "BOOKING_NOT_OWNED_BY_SENDER",
                    "Booking does not belong to the sender.");
            if (booking.TripId != command.TripId)
                throw new CodedConflictException(
                    "BOOKING_NOT_FOR_THIS_TRIP",
                    "Booking is not associated with the specified trip.");
            if (booking.Status != "CONFIRMED")
                throw new CodedConflictException(
                    "BOOKING_NOT_ATTACHABLE",
                    $"Booking is in status '{booking.Status}' and cannot be attached to a parcel.");
            if (booking.ActiveTicketCount <= 0)
                throw new CodedConflictException(
                    "BOOKING_NOT_ATTACHABLE",
                    "Booking has no active ticket that can be attached to a parcel.");
        }

        var tripOutcome = await _tripClient.GetTripParcelSnapshotAsync(command.TripId, cancellationToken);
        switch (tripOutcome.Kind)
        {
            case TripSnapshotOutcomeKind.TripNotFound:
                throw new CodedNotFoundException(
                    "TRIP_NOT_FOUND",
                    $"Trip with id '{command.TripId}' not found.");
            case TripSnapshotOutcomeKind.TransportError:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    tripOutcome.ErrorMessage ?? "Trip service unavailable.");
        }

        var trip = tripOutcome.Snapshot!;
        if (trip.Status != "SCHEDULED" && trip.Status != "BOARDING")
            throw new CodedConflictException(
                "TRIP_NOT_ACCEPTING_PARCEL",
                $"Trip '{command.TripId}' is in status '{trip.Status}' and is not accepting parcels.");

        if (command.DropoffStopId.HasValue)
        {
            var dropoffStop = trip.Stops.FirstOrDefault(stop => stop.StopId == command.DropoffStopId.Value);
            if (dropoffStop is null)
                throw new CodedValidationException(
                    "DROP_OFF_STOP_NOT_FOUND",
                    $"Drop-off stop '{command.DropoffStopId}' not found in trip '{command.TripId}'.");

            if (!dropoffStop.AllowDropoff)
                throw new CodedValidationException(
                    "DROP_OFF_STOP_NOT_ALLOWED",
                    $"Drop-off stop '{command.DropoffStopId}' does not allow drop-off.");
        }

        if (!Enum.TryParse<ParcelSizeCategory>(command.SizeCategory, ignoreCase: true, out var sizeCategory))
            throw new CodedValidationException(
                "INVALID_SIZE_CATEGORY",
                $"'{command.SizeCategory}' is not a valid ParcelSizeCategory.");

        var parcelCode = await GenerateParcelCodeAsync(cancellationToken);

        var finalDescription = command.ItemName is not null
            ? command.Description is not null
                ? $"{command.ItemName}\n{command.Description}"
                : command.ItemName
            : command.Description;

        if (!Enum.TryParse<ParcelDeliveryMethod>(command.DeliveryMethod, ignoreCase: true, out var deliveryMethod))
            throw new CodedValidationException(
                "INVALID_DELIVERY_METHOD",
                $"'{command.DeliveryMethod}' is not a valid ParcelDeliveryMethod.");

        var recipientPhone = PhoneNumber.Normalize(command.RecipientPhone);

        Money priceVnd;
        if (sizeCategory == ParcelSizeCategory.EXTRA_LARGE)
        {
            priceVnd = Money.FromRaw(0);
        }
        else
        {
            var fare = await _fareRepository.FindByCompositeAsync(trip.RouteId, sizeCategory, cancellationToken);
            if (fare is null)
                throw new CodedValidationException(
                    "FARE_NOT_CONFIGURED",
                    $"No fare configured for route '{trip.RouteId}' and size category '{command.SizeCategory}'.");
            priceVnd = fare.PriceVnd;
        }

        var parcel = sizeCategory == ParcelSizeCategory.EXTRA_LARGE
            ? ParcelEntity.CreatePendingOperatorReview(
                parcelCode,
                command.SenderUserId,
                command.RecipientUserId,
                command.RecipientName,
                recipientPhone,
                command.RecipientEmail,
                trip.OperatorId,
                command.TripId,
                command.DropoffStopId,
                command.BookingId,
                finalDescription,
                command.PhotoUrl,
                sizeCategory,
                command.EstimatedWeightKg,
                deliveryMethod,
                priceVnd)
            : ParcelEntity.CreatePendingPayment(
                parcelCode,
                command.SenderUserId,
                command.RecipientUserId,
                command.RecipientName,
                recipientPhone,
                command.RecipientEmail,
                trip.OperatorId,
                command.TripId,
                command.DropoffStopId,
                command.BookingId,
                finalDescription,
                command.PhotoUrl,
                sizeCategory,
                command.EstimatedWeightKg,
                deliveryMethod,
                priceVnd);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _parcelRepository.AddAsync(parcel, cancellationToken);
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.Created,
                new { parcelId = parcel.Id, tripId = parcel.TripId, senderUserId = parcel.SenderUserId, recipientUserId = parcel.RecipientUserId },
                cancellationToken);

            if (sizeCategory == ParcelSizeCategory.EXTRA_LARGE)
            {
                await ParcelOutboxEvents.EnqueueAsync(
                    _outbox,
                    ParcelOutboxEvents.ReviewRequested,
                    new { parcelId = parcel.Id, operatorId = parcel.OperatorId },
                    cancellationToken);
            }

            await _statsRepository.UpsertIncrementAsync(
                parcel.OperatorId,
                DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
                totalParcels: 1,
                totalLoaded: 0,
                totalDelivered: 0,
                totalRejected: 0,
                totalReturned: 0,
                totalRevenue: 0,
                totalRefunded: 0,
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        string? paymentRedirectUrl = null;

        if (sizeCategory != ParcelSizeCategory.EXTRA_LARGE)
        {
            var idempotencyKey = $"parcel:deposit:{parcel.Id}";
            var outcome = await _paymentClient.ChargeParcelPaymentAsync(
                "PARCEL",
                parcel.Id,
                command.SenderUserId,
                priceVnd.Amount,
                command.PaymentMethod,
                idempotencyKey,
                cancellationToken);

            if (outcome.Kind == ChargeOutcomeKind.InsufficientFunds)
            {
                throw new CodedValidationException(
                    "INSUFFICIENT_FUNDS",
                    outcome.ErrorMessage ?? "Insufficient wallet balance.");
            }

            if (outcome.Kind == ChargeOutcomeKind.TransportError)
            {
                throw new ParcelDependencyUnavailableException(
                    "PAYMENT_SERVICE_ERROR",
                    outcome.ErrorMessage ?? "Payment service unavailable.");
            }

            paymentRedirectUrl = outcome.Result?.PaymentRedirectUrl;
        }

        return new CreateParcelResponse(
            parcel.Id,
            parcel.ParcelCode,
            parcel.Status.ToString(),
            priceVnd.Amount,
            paymentRedirectUrl);
    }

    private async Task<string> GenerateParcelCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var code = ParcelCodeGenerator.Generate(DateTimeOffset.UtcNow);
            var existing = await _parcelRepository.FindByParcelCodeAsync(code, cancellationToken);
            if (existing is null)
            {
                return code;
            }
        }

        throw new CodedConflictException(
            "PARCEL_CODE_COLLISION",
            "Failed to generate a unique parcel code after 3 attempts.");
    }
}
