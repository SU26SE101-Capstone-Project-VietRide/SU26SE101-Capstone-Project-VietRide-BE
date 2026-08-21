using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;

public sealed record MatchUnidentifiedPackageCommand(
    Guid PackageId,
    Guid ParcelId,
    Guid OperatorId,
    Guid ActorUserId) : IRequest<UnidentifiedPackageResponse>;
