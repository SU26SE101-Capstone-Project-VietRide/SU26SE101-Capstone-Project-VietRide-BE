using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Create;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Update;

public sealed class UpdateParcelRouteFareCommandHandler : IRequestHandler<UpdateParcelRouteFareCommand, ParcelRouteFareResponse>
{
    private readonly IParcelRouteFareRepository _repository;
    private readonly ITripServiceClient _tripClient;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateParcelRouteFareCommandHandler(
        IParcelRouteFareRepository repository,
        ITripServiceClient tripClient,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _tripClient = tripClient;
        _unitOfWork = unitOfWork;
    }

    public async Task<ParcelRouteFareResponse> Handle(UpdateParcelRouteFareCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ParcelSizeCategory>(command.SizeCategory, ignoreCase: true, out var sizeCategory))
            throw new CodedValidationException(
                "INVALID_SIZE_CATEGORY",
                $"'{command.SizeCategory}' is not a valid ParcelSizeCategory.");

        var ownership = await _tripClient.ValidateRouteOwnershipAsync(
            command.RouteId,
            command.OperatorId,
            cancellationToken);

        if (ownership.Kind == RouteOwnershipOutcomeKind.TransportError)
            throw new ParcelDependencyUnavailableException(
                "ROUTE_OWNERSHIP_UNVERIFIABLE",
                ownership.ErrorMessage ?? "Unable to verify route ownership.");

        if (ownership.Kind == RouteOwnershipOutcomeKind.RouteNotFound)
            throw new CodedNotFoundException(
                "ROUTE_NOT_FOUND",
                $"Route with id '{command.RouteId}' not found.");

        var fare = await _repository.FindByCompositeAsync(
            command.RouteId,
            sizeCategory,
            cancellationToken);

        if (fare is null || fare.OperatorId != command.OperatorId)
            throw new CodedNotFoundException(
                "FARE_NOT_FOUND",
                $"Fare for route '{command.RouteId}' and size '{command.SizeCategory}' not found.");

        if (command.PriceVnd.HasValue)
        {
            var floored = (command.PriceVnd.Value / 1000) * 1000;
            if (floored < 1000)
                throw new CodedValidationException("VALIDATION_ERROR", "Price must be at least 1000 VND after flooring.");
            fare.UpdatePrice(Money.FromRaw(floored));
        }

        if (command.EffectiveFrom.HasValue || command.EffectiveUntil is not null)
        {
            var effectiveFrom = command.EffectiveFrom ?? fare.EffectiveFrom;
            var effectiveUntil = command.EffectiveUntil ?? fare.EffectiveUntil;

            if (effectiveUntil.HasValue && effectiveUntil <= effectiveFrom)
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "EffectiveUntil must be after EffectiveFrom.");

            fare.UpdateEffectiveWindow(effectiveFrom, effectiveUntil);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ParcelRouteFareResponse(
            fare.RouteId,
            fare.SizeCategory.ToString(),
            fare.OperatorId,
            fare.PriceVnd.Amount,
            fare.EffectiveFrom,
            fare.EffectiveUntil,
            fare.CreatedAt,
            fare.UpdatedAt);
    }
}
