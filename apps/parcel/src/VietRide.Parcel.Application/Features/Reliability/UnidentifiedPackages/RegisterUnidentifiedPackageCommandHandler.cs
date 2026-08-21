using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;

public sealed class RegisterUnidentifiedPackageCommandHandler
    : IRequestHandler<RegisterUnidentifiedPackageCommand, UnidentifiedPackageResponse>
{
    private readonly IParcelReliabilityRepository _reliability;

    public RegisterUnidentifiedPackageCommandHandler(IParcelReliabilityRepository reliability)
    {
        _reliability = reliability;
    }

    public async Task<UnidentifiedPackageResponse> Handle(
        RegisterUnidentifiedPackageCommand command,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ParcelCustodyLocationType>(command.LocationType, true, out var locationType))
            throw new CodedValidationException("VALIDATION_ERROR", "LocationType is invalid.");
        UnidentifiedParcelPackage package;
        try
        {
            package = UnidentifiedParcelPackage.Create(
                command.TemporaryExceptionTag,
                command.OperatorId,
                command.TripId,
                locationType,
                command.LocationId,
                command.LocationSnapshot,
                command.Description,
                command.ObservedWeightKg,
                command.EvidenceReferences,
                command.ActorUserId);
        }
        catch (ArgumentException exception)
        {
            throw new CodedValidationException("VALIDATION_ERROR", exception.Message);
        }

        await _reliability.AddUnidentifiedPackageAsync(package, cancellationToken);
        return Map(package);
    }

    internal static UnidentifiedPackageResponse Map(UnidentifiedParcelPackage package)
        => UnidentifiedPackageReadModelMapper.Map(package, null, null);
}
