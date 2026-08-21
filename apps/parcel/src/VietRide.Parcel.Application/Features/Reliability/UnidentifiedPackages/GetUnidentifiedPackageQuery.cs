using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;

public sealed record GetUnidentifiedPackageQuery(Guid PackageId, Guid OperatorId)
    : IRequest<UnidentifiedPackageResponse>;
