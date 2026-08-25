using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Features.BookingTransfers.EscalatePendingTransfers;

namespace VietRide.Booking.Infrastructure.Jobs;

public sealed class BookingTransferEscalationJob(
    IMediator mediator,
    ILogger<BookingTransferEscalationJob> logger)
{
    public const string RecurringJobId = "booking.transfer-escalation";

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var count = await mediator.Send(
            new EscalatePendingBookingTransfersCommand(),
            cancellationToken);
        logger.LogInformation(
            "Booking transfer escalation completed. Escalated {EscalatedGroupCount} group(s).",
            count);
    }
}
