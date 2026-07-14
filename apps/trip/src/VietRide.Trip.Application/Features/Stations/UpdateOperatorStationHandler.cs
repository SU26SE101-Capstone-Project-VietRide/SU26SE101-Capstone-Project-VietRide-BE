using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class UpdateOperatorStationHandler : IRequestHandler<UpdateOperatorStationCommand, OperatorStationDto>
{
    private readonly IIdentityInternalClient identityClient;
    private readonly IOperatorStationRepository mappings;
    private readonly IStationRepository stations;
    private readonly IUnitOfWork unitOfWork;

    public UpdateOperatorStationHandler(IIdentityInternalClient identityClient, IOperatorStationRepository mappings, IStationRepository stations, IUnitOfWork unitOfWork)
    {
        this.identityClient = identityClient;
        this.mappings = mappings;
        this.stations = stations;
        this.unitOfWork = unitOfWork;
    }

    public async Task<OperatorStationDto> Handle(UpdateOperatorStationCommand request, CancellationToken cancellationToken)
    {
        await ValidateOperatorAsync(request.OperatorId, cancellationToken);
        var mapping = mappings.Query().FirstOrDefault(x => x.OperatorId == request.OperatorId && x.StationId == request.StationId)
            ?? throw new CodedNotFoundException("STATION_NOT_FOUND", "Operator station was not found.");
        var station = stations.QueryNoTracking().FirstOrDefault(x => x.Id == request.StationId && x.DeletedAt == null)
            ?? throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");

        mapping.UpdateDetails(request.DisplayNameOverride, request.CounterLocation, request.ContactPhone, request.Instructions);
        mappings.Update(mapping);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(mapping, station);
    }

    private async Task ValidateOperatorAsync(Guid operatorId, CancellationToken cancellationToken)
    {
        var result = await identityClient.ValidateOperatorCanWriteAsync(operatorId, cancellationToken);
        if (result.IsAllowed)
        {
            return;
        }

        if (result.FailureStatusCode == 403)
        {
            throw new ForbiddenException("FORBIDDEN", result.Message ?? "Operator cannot update stations.");
        }

        throw new CodedValidationException("VALIDATION_ERROR", result.Message ?? "Operator validation failed.");
    }

    internal static OperatorStationDto ToDto(Domain.Entities.OperatorStation mapping, Domain.Entities.Station station)
        => new(mapping.Id, mapping.OperatorId, mapping.StationId, StationMapper.ToDto(station), mapping.DisplayNameOverride,
            mapping.CounterLocation, mapping.ContactPhone, mapping.Instructions, mapping.IsActive, mapping.CreatedAt, mapping.UpdatedAt);
}
