using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;

public sealed class MatchUnidentifiedPackageCommandHandler
    : IRequestHandler<MatchUnidentifiedPackageCommand, UnidentifiedPackageResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelCustodyService _custody;
    private readonly IClock _clock;

    public MatchUnidentifiedPackageCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IParcelCustodyService custody,
        IClock clock)
    {
        _parcels = parcels;
        _reliability = reliability;
        _custody = custody;
        _clock = clock;
    }

    public async Task<UnidentifiedPackageResponse> Handle(
        MatchUnidentifiedPackageCommand command,
        CancellationToken cancellationToken)
    {
        var package = await _reliability.GetUnidentifiedPackageAsync(command.PackageId, cancellationToken)
            ?? throw new CodedNotFoundException("UNIDENTIFIED_PACKAGE_NOT_FOUND", "Unidentified package was not found.");
        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (package.OperatorId != command.OperatorId || parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Package and parcel must belong to this operator.");

        package.Match(parcel.Id, command.ActorUserId, _clock.UtcNow);
        await _reliability.UpdateUnidentifiedPackageAsync(package, cancellationToken);
        await _custody.AppendAsync(
            parcel,
            ParcelCustodyEventType.IDENTIFIED_MANUALLY,
            package.LocationType,
            package.LocationId,
            package.LocationSnapshot,
            command.ActorUserId,
            "OPERATOR_STAFF",
            "UNIDENTIFIED_PACKAGE_MATCH",
            $"unidentified:{package.Id:D}:match",
            null,
            $"Matched temporary tag {package.TemporaryExceptionTag}.",
            cancellationToken);

        return RegisterUnidentifiedPackageCommandHandler.Map(package);
    }
}
