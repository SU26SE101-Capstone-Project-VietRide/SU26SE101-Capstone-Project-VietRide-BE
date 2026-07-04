using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Features.Parcels.SendDeliveryPendingConfirmReminders;

namespace VietRide.Parcel.Infrastructure.Jobs;

public sealed class ParcelDeliveryPendingConfirmReminderJob
{
    public const string RecurringJobId = "parcel.delivery-pending-confirm-reminder";

    private readonly IMediator _mediator;
    private readonly ILogger<ParcelDeliveryPendingConfirmReminderJob> _logger;

    public ParcelDeliveryPendingConfirmReminderJob(IMediator mediator, ILogger<ParcelDeliveryPendingConfirmReminderJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var count = await _mediator.Send(new SendDeliveryPendingConfirmRemindersCommand(), cancellationToken);

        _logger.LogInformation(
            "Parcel delivery pending confirmation reminder completed. Re-alerted {ReminderCount} parcel(s).",
            count);
    }
}
