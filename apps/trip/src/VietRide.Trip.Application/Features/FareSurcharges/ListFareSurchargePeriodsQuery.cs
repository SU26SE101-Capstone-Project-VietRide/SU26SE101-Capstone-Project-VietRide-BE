using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed record ListFareSurchargePeriodsQuery(
    Guid OperatorId,
    int? Page,
    int? PageSize) : IRequest<PagedResult<FareSurchargePeriodDto>>;
