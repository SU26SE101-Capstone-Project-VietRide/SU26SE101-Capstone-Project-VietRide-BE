using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Features.Parcels.ExpireParcelAdditionalPayment;

namespace VietRide.Parcel.Infrastructure.Jobs;

public sealed class ParcelAdditionalPaymentTimeoutJob
{
    public const string RecurringJobId = "parcel.additional-payment-timeout";

    private readonly IMediator _mediator;
    private readonly ILogger<ParcelAdditionalPaymentTimeoutJob> _logger;

    public ParcelAdditionalPaymentTimeoutJob(IMediator mediator, ILogger<ParcelAdditionalPaymentTimeoutJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var count = await _mediator.Send(new ExpireParcelAdditionalPaymentCommand(), cancellationToken);

        _logger.LogInformation(
            "Parcel additional-payment timeout scan completed. Auto-rejected {RejectedCount} parcel(s).",
            count);
    }
}
