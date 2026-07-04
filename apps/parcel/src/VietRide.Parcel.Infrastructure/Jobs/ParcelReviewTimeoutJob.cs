using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Features.Parcels.ExpireParcelReview;

namespace VietRide.Parcel.Infrastructure.Jobs;

public sealed class ParcelReviewTimeoutJob
{
    public const string RecurringJobId = "parcel.review-timeout";

    private readonly IMediator _mediator;
    private readonly ILogger<ParcelReviewTimeoutJob> _logger;

    public ParcelReviewTimeoutJob(IMediator mediator, ILogger<ParcelReviewTimeoutJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var count = await _mediator.Send(new ExpireParcelReviewCommand(), cancellationToken);

        _logger.LogInformation(
            "Parcel review timeout scan completed. Auto-rejected {RejectedCount} parcel(s).",
            count);
    }
}
