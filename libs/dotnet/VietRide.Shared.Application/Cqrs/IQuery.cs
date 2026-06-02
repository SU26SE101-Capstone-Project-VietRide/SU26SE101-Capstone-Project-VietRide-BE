using MediatR;

namespace VietRide.Shared.Application.Cqrs;

/// <summary>Marker for read-only MediatR requests that must not open a transaction.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>
{
}
