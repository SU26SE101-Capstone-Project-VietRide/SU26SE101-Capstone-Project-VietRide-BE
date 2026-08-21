using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;

public sealed record ListUnidentifiedPackagesQuery(
    Guid OperatorId,
    string? Status,
    string? Search,
    Guid? TripId,
    int Page,
    int PageSize) : IRequest<PagedResult<UnidentifiedPackageResponse>>;
