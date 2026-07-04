using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Create;

public sealed class CreateParcelRouteFareCommandHandler : IRequestHandler<CreateParcelRouteFareCommand, ParcelRouteFareResponse>
{
    private readonly IParcelRouteFareRepository _repository;
    private readonly ITripServiceClient _tripClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateParcelRouteFareCommandHandler(
        IParcelRouteFareRepository repository,
        ITripServiceClient tripClient,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _repository = repository;
        _tripClient = tripClient;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<ParcelRouteFareResponse> Handle(CreateParcelRouteFareCommand command, CancellationToken cancellationToken)
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

        var existing = await _repository.FindByCompositeAsync(
            command.RouteId,
            sizeCategory,
            cancellationToken);

        if (existing is not null)
            throw new CodedConflictException(
                "FARE_ALREADY_EXISTS",
                $"A fare for route '{command.RouteId}' and size '{command.SizeCategory}' already exists.");

        if (command.PriceVnd <= 0)
            throw new CodedValidationException("VALIDATION_ERROR", "Price must be positive.");
        var price = Money.FromRaw(command.PriceVnd);

        var fare = ParcelRouteFare.Create(
            command.RouteId,
            sizeCategory,
            command.OperatorId,
            price,
            command.EffectiveFrom,
            command.EffectiveUntil);

        await _repository.AddAsync(fare, cancellationToken);
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
