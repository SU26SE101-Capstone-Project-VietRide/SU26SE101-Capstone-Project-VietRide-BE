using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;

public sealed record ListUnidentifiedMatchCandidatesQuery(Guid PackageId, Guid OperatorId, int Limit = 20)
    : IRequest<IReadOnlyList<UnidentifiedPackageMatchCandidateResponse>>;
