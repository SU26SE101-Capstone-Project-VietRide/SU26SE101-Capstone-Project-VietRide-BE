using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Parcel.Application.Features.Parcels.ResendDeliveryEmail;

public sealed class ResendDeliveryEmailCommandHandler
    : IRequestHandler<ResendDeliveryEmailCommand, ResendDeliveryEmailResponse>
{
    private static readonly TimeSpan DeliveryConfirmWindow = TimeSpan.FromHours(48);
    private static readonly TimeSpan DeliveryRejectedUndoWindow = TimeSpan.FromMinutes(15);

    private readonly IParcelRepository _parcelRepository;
    private readonly IParcelDeliveryTokenRepository _deliveryTokenRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IParcelDeliveryEmailClient _deliveryEmailClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ResendDeliveryEmailCommandHandler(
        IParcelRepository parcelRepository,
        IParcelDeliveryTokenRepository deliveryTokenRepository,
        ITripServiceClient tripClient,
        IParcelDeliveryEmailClient deliveryEmailClient,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository,
        IUnitOfWork unitOfWork)
    {
        _parcelRepository = parcelRepository;
        _deliveryTokenRepository = deliveryTokenRepository;
        _tripClient = tripClient;
        _deliveryEmailClient = deliveryEmailClient;
        _outbox = outbox;
        _statsRepository = statsRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ResendDeliveryEmailResponse> Handle(
        ResendDeliveryEmailCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(
            command.ParcelId,
            cancellationToken)
            ?? throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel '{command.ParcelId}' not found.");

        if (parcel.OperatorId != command.OperatorId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Parcel does not belong to this operator.");
        }

        await EnsureActorAuthorizedAsync(
            parcel.TripId,
            command,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(parcel.RecipientEmail))
        {
            throw new CodedValidationException(
                "PARCEL_RECIPIENT_EMAIL_REQUIRED",
                "Recipient email is required to resend the delivery confirmation link.");
        }

        var now = DateTimeOffset.UtcNow;
        var expectedStatus = ValidateStatus(parcel.Status, parcel.RejectedAt, now);
        var expectedActiveToken = await _deliveryTokenRepository.FindActiveByParcelIdAsync(
            parcel.Id,
            cancellationToken)
            ?? throw new CodedConflictException(
                "RESOURCE_CONFLICT",
                $"Parcel '{parcel.Id}' has no active delivery token to rotate.");
        var rawToken = Guid.NewGuid();
        var expiresAt = now.Add(DeliveryConfirmWindow);
        var deliveryToken = ParcelDeliveryToken.Issue(
            parcel.Id,
            DeliveryTokenHasher.Hash(rawToken),
            expiresAt,
            command.ActorUserId,
            ParcelDeliveryTokenIssueReason.RESEND,
            now);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var snapshot = await _parcelRepository.TryPrepareDeliveryResendAsync(
                parcel.Id,
                expectedStatus,
                expectedActiveToken.Id,
                now,
                cancellationToken)
                ?? throw new CodedConflictException(
                    "RESOURCE_CONFLICT",
                    $"Parcel '{parcel.Id}' delivery token or status changed concurrently; cannot resend delivery email.");

            await _deliveryTokenRepository.RevokeActiveAsync(
                parcel.Id,
                now,
                cancellationToken);
            await _deliveryTokenRepository.AddAsync(deliveryToken, cancellationToken);

            if (expectedStatus == ParcelStatus.DELIVERY_REJECTED)
            {
                await ParcelOutboxEvents.EnqueueAsync(
                    _outbox,
                    ParcelOutboxEvents.DeliveryRejectUndone,
                    new { parcelId = snapshot.ParcelId },
                    cancellationToken);

                await _statsRepository.UpsertIncrementAsync(
                    snapshot.OperatorId,
                    DateOnly.FromDateTime(now.UtcDateTime),
                    0, 0, 0, -1, 0, 0, 0,
                    cancellationToken);
            }

            await _deliveryEmailClient.SendDeliveryLinkAsync(
                new ParcelDeliveryEmailRequest(
                    deliveryToken.Id,
                    parcel.RecipientEmail,
                    rawToken,
                    parcel.ParcelCode,
                    expiresAt),
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return new ResendDeliveryEmailResponse(
            parcel.Id,
            ParcelStatus.DELIVERED_PENDING_CONFIRM.ToString(),
            expiresAt);
    }

    private static ParcelStatus ValidateStatus(
        ParcelStatus status,
        DateTimeOffset? rejectedAt,
        DateTimeOffset now)
    {
        if (status == ParcelStatus.DELIVERED_PENDING_CONFIRM)
        {
            return status;
        }

        if (status == ParcelStatus.DELIVERY_REJECTED)
        {
            if (!rejectedAt.HasValue
                || rejectedAt.Value.Add(DeliveryRejectedUndoWindow) <= now)
            {
                throw new BadRequestException(
                    "PARCEL_DELIVERY_REJECTED_WINDOW_EXPIRED",
                    "The delivery rejection undo window has expired.");
            }

            return status;
        }

        throw new BadRequestException(
            "PARCEL_NOT_PENDING_CONFIRM",
            $"Parcel is in status '{status}' and cannot resend a delivery confirmation email.");
    }

    private async Task EnsureActorAuthorizedAsync(
        Guid tripId,
        ResendDeliveryEmailCommand command,
        CancellationToken cancellationToken)
    {
        var role = command.ActorRole.Trim().ToUpperInvariant();
        if (role is "OPERATOR_ADMIN" or "OPERATOR_STAFF")
        {
            return;
        }

        if (role is not ("DRIVER" or "ASSISTANT"))
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Only operator staff or assigned trip crew can resend a delivery email.");
        }

        var authorization = await _tripClient.AuthorizeCrewForTripAsync(
            tripId,
            command.ActorUserId,
            command.OperatorId,
            role,
            cancellationToken);

        switch (authorization.Kind)
        {
            case TripCrewAuthorizationOutcomeKind.Authorized:
                return;
            case TripCrewAuthorizationOutcomeKind.Denied:
            case TripCrewAuthorizationOutcomeKind.TripNotFound:
                throw new ForbiddenException(
                    "FORBIDDEN",
                    "Only the Driver or Assistant assigned to this parcel's trip can resend a delivery email.");
            default:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    authorization.ErrorMessage ?? "Trip service unavailable.");
        }
    }
}
