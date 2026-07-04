using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Features.Parcels.AutoRejectPendingParcel;

namespace VietRide.Parcel.Infrastructure.Jobs;

public sealed class ParcelPendingAutoRejectJob
{
    public const string RecurringJobId = "parcel.pending-auto-reject";

    private readonly IMediator _mediator;
    private readonly ILogger<ParcelPendingAutoRejectJob> _logger;

    public ParcelPendingAutoRejectJob(IMediator mediator, ILogger<ParcelPendingAutoRejectJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var count = await _mediator.Send(new AutoRejectPendingParcelCommand(), cancellationToken);

        _logger.LogInformation(
            "Parcel pending auto-reject scan completed. Rejected {RejectedCount} parcel(s).",
            count);
    }
}
