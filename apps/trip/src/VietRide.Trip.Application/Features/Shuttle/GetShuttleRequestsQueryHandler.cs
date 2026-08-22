using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Time;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Trips.Operations;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class GetShuttleRequestsQueryHandler
    : IRequestHandler<GetShuttleRequestsQuery, ShuttleRequestPage>
{
    private readonly IShuttleDispatchService _service;
    private readonly IIdentityInternalClient? _identity;

    public GetShuttleRequestsQueryHandler(IShuttleDispatchService service, IIdentityInternalClient? identity = null)
    {
        _service = service;
        _identity = identity;
    }

    public async Task<ShuttleRequestPage> Handle(
        GetShuttleRequestsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "Invalid paging values.");
        if (request.From.HasValue && request.To.HasValue && request.From > request.To)
            throw new CodedValidationException("VALIDATION_ERROR", "from must be on or before to.");
        if (request.Search?.Length > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "search must not exceed 100 characters.");
        IReadOnlyCollection<Guid> userIds = [];
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            if (_identity is null)
                throw new TripIdentityUnavailableException("Identity user search is unavailable.");
            var outcome = await _identity.SearchUserIdsAsync(request.Search.Trim(), cancellationToken);
            if (outcome.TooBroad)
                throw new CodedValidationException("SEARCH_TOO_BROAD", "Search matched more than 1,000 users.");
            if (!outcome.Succeeded)
                throw new TripIdentityUnavailableException(outcome.Message ?? "Identity user search is unavailable.");
            userIds = outcome.UserIds;
        }
        var extended = request.From.HasValue || request.To.HasValue || request.MainTripId.HasValue
            || !string.IsNullOrWhiteSpace(request.Search);
        return extended
            ? await _service.GetPendingFilteredAsync(
                request.OperatorId, request.Page, request.PageSize,
                request.From.HasValue ? BusinessTime.ToUtc(request.From.Value, TimeOnly.MinValue) : null,
                request.To.HasValue ? BusinessTime.ToUtc(request.To.Value.AddDays(1), TimeOnly.MinValue) : null,
                request.MainTripId, request.Search, userIds, cancellationToken)
            : await _service.GetPendingAsync(request.OperatorId, request.Page, request.PageSize, cancellationToken);
    }
}
