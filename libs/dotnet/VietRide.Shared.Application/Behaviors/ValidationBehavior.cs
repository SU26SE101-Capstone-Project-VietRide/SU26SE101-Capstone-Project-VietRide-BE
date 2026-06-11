using FluentValidation;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using ValidationException = VietRide.Shared.Application.Exceptions.ValidationException;

namespace VietRide.Shared.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs FluentValidation validators for the incoming
/// request before the handler executes. Throws <see cref="ValidationException"/> with
/// field-level errors so the shared <c>ApiResponseExceptionFilter</c> maps it to
/// HTTP 422 Unprocessable Entity.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
        {
            var errors = failures
                .Select(f => new ValidationError(f.PropertyName, f.ErrorMessage))
                .ToList();

            var errorCodes = failures
                .Select(f => f.ErrorCode)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (errorCodes.Count == 1 && IsUpperSnakeCase(errorCodes[0]))
            {
                throw new CodedValidationException(
                    errorCodes[0],
                    "One or more validation errors occurred.",
                    errors);
            }

            throw new ValidationException("One or more validation errors occurred.", errors);
        }

        return await next();
    }

    private static bool IsUpperSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var previousWasUnderscore = true;
        foreach (var c in value)
        {
            if (c == '_')
            {
                if (previousWasUnderscore)
                {
                    return false;
                }

                previousWasUnderscore = true;
                continue;
            }

            if (c is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9'))
            {
                return false;
            }

            previousWasUnderscore = false;
        }

        return !previousWasUnderscore;
    }
}
