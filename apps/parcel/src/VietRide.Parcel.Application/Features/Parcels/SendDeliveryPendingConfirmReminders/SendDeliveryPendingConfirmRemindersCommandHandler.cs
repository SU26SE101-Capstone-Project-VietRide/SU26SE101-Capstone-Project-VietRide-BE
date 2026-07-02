using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.SendDeliveryPendingConfirmReminders;

public sealed class SendDeliveryPendingConfirmRemindersCommandHandler
    : IRequestHandler<SendDeliveryPendingConfirmRemindersCommand, int>
{
    private static readonly TimeSpan DeliveryPendingConfirmWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromHours(23);
    private const int MaxBatch = 200;

    private readonly IParcelRepository _parcelRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<SendDeliveryPendingConfirmRemindersCommandHandler> _logger;

    public SendDeliveryPendingConfirmRemindersCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<SendDeliveryPendingConfirmRemindersCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> Handle(
        SendDeliveryPendingConfirmRemindersCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var parcels = await _parcelRepository.TryBulkReissueDeliveryPendingConfirmRemindersAsync(
                now.Subtract(DeliveryPendingConfirmWindow),
                now.Subtract(ReminderInterval),
                now,
                MaxBatch,
                cancellationToken);

            foreach (var parcel in parcels)
            {
                await ParcelOutboxEvents.EnqueueAsync(
                    _outbox,
                    ParcelOutboxEvents.DeliveredPendingConfirm,
                    BuildReminderPayload(parcel),
                    cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);

            if (parcels.Count > 0)
            {
                _logger.LogInformation(
                    "Sent delivery pending confirmation reminders for {ReminderCount} parcel(s).",
                    parcels.Count);
            }

            return parcels.Count;
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static Dictionary<string, object?> BuildReminderPayload(ParcelEventSnapshot parcel)
    {
        var payload = new Dictionary<string, object?>
        {
            ["parcelId"] = parcel.ParcelId,
            ["parcelCode"] = parcel.ParcelCode,
            ["operatorId"] = parcel.OperatorId,
            ["tripId"] = parcel.TripId,
            ["deliveryToken"] = parcel.DeliveryToken,
            ["deliveryTokenExpiresAt"] = parcel.DeliveryTokenExpiresAt,
        };

        if (parcel.RecipientUserId.HasValue)
        {
            payload["userId"] = parcel.RecipientUserId.Value;
            payload["recipientUserIds"] = new[] { parcel.RecipientUserId.Value };
        }

        return payload;
    }
}
