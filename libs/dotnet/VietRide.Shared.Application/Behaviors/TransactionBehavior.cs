using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Shared.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that wraps the handler in a database transaction.
/// If an <see cref="IUnitOfWork"/> is registered it is used directly; otherwise
/// the behavior is a no-op and the handler manages its own transaction.
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
        if (_unitOfWork is null)
        {
            return await next();
        }

        var requestName = typeof(TRequest).Name;

        _logger.LogDebug("Beginning transaction for {RequestName}", requestName);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var response = await next();
            await _unitOfWork.CommitAsync(cancellationToken);
            _logger.LogDebug("Committed transaction for {RequestName}", requestName);
            return response;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            _logger.LogWarning(ex, "Rolled back transaction for {RequestName}", requestName);
            throw;
        }
    }
}
