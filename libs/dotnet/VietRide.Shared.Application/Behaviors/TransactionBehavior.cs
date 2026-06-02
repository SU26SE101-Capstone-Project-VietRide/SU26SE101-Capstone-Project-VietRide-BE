using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Shared.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that wraps command handlers in a database transaction.
/// Query requests bypass the transaction; if an <see cref="IUnitOfWork"/> is registered,
/// it owns the retry-safe transaction boundary, otherwise the behavior is a no-op.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IUnitOfWork? _unitOfWork;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(
        ILogger<TransactionBehavior<TRequest, TResponse>> logger,
        IUnitOfWork? unitOfWork = null)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_unitOfWork is null || IsQueryRequest())
        {
            return await next();
        }

        var requestName = typeof(TRequest).Name;

        _logger.LogDebug("Beginning transaction for {RequestName}", requestName);

        try
        {
            var response = await _unitOfWork.ExecuteInTransactionAsync<TResponse>(() => next(), cancellationToken);
            _logger.LogDebug("Committed transaction for {RequestName}", requestName);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rolled back transaction for {RequestName}", requestName);
            throw;
        }
    }

    private static bool IsQueryRequest()
        => typeof(TRequest).GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
}
