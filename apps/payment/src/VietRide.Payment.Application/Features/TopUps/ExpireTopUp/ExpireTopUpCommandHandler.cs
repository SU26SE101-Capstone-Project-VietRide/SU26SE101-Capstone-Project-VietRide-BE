using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.TopUps.ExpireTopUp;

public sealed class ExpireTopUpCommandHandler : IRequestHandler<ExpireTopUpCommand, ExpireTopUpResult>
{
    private static readonly TimeSpan TopUpTimeout = TimeSpan.FromMinutes(10);

    private readonly ITopUpRequestRepository _topUpRequests;
    private readonly IClock _clock;
    private readonly ILogger<ExpireTopUpCommandHandler> _logger;

    public ExpireTopUpCommandHandler(
        ITopUpRequestRepository topUpRequests,
        IClock clock,
        ILogger<ExpireTopUpCommandHandler> logger)
    {
        _topUpRequests = topUpRequests;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ExpireTopUpResult> Handle(
        ExpireTopUpCommand request,
        CancellationToken cancellationToken)
    {
        var now = request.Now ?? _clock.UtcNow;
        var expiresBefore = now - TopUpTimeout;

        var expiredCount = await _topUpRequests.ExpirePendingOlderThanAsync(
            expiresBefore,
            now,
            cancellationToken);

        if (expiredCount > 0)
        {
            _logger.LogInformation(
                "Expired {TopUpRequestCount} pending top-up requests older than {ExpiresBefore}.",
                expiredCount,
                expiresBefore);
        }

        return new ExpireTopUpResult(expiredCount);
    }
}
