using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Batch;

public sealed class BatchParcelRouteFareCommandHandler
    : IRequestHandler<BatchParcelRouteFareCommand, BatchParcelRouteFareResponse>
{
    private readonly IParcelRouteFareRepository _repository;
    private readonly ITripServiceClient _tripClient;
    private readonly IUnitOfWork _unitOfWork;

    public BatchParcelRouteFareCommandHandler(
        IParcelRouteFareRepository repository,
        ITripServiceClient tripClient,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _tripClient = tripClient;
        _unitOfWork = unitOfWork;
    }

    public async Task<BatchParcelRouteFareResponse> Handle(
        BatchParcelRouteFareCommand command,
        CancellationToken cancellationToken)
    {
        var parsedItems = ParseAndValidateItems(command);
        var effectiveFromUtc = command.EffectiveFrom.ToUniversalTime();
        var effectiveUntilUtc = command.EffectiveUntil?.ToUniversalTime();
        var ownership = await _tripClient.ValidateRouteOwnershipAsync(
            command.RouteId,
            command.OperatorId,
            cancellationToken);

        if (ownership.Kind == RouteOwnershipOutcomeKind.TransportError)
        {
            throw new ParcelDependencyUnavailableException(
                "ROUTE_OWNERSHIP_UNVERIFIABLE",
                ownership.ErrorMessage ?? "Unable to verify route ownership.");
        }

        if (ownership.Kind == RouteOwnershipOutcomeKind.RouteNotFound)
        {
            throw new CodedNotFoundException(
                "ROUTE_NOT_FOUND",
                $"Route with id '{command.RouteId}' not found.");
        }

        var categories = parsedItems.Select(item => item.SizeCategory).ToArray();
        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _repository.AcquireRouteBatchLockAsync(command.RouteId, cancellationToken);
            var existingRows = await _repository.FindByRouteAndSizesAsync(
                command.RouteId,
                categories,
                cancellationToken);
            var existingByCategory = existingRows.ToDictionary(fare => fare.SizeCategory);
            var createdRows = new List<ParcelRouteFare>();
            var results = new List<(ParcelRouteFare Fare, bool Created)>(parsedItems.Count);

            foreach (var item in parsedItems)
            {
                if (existingByCategory.TryGetValue(item.SizeCategory, out var existing))
                {
                    existing.AssignOperator(command.OperatorId);
                    existing.UpdatePrice(Money.FromRaw(item.PriceVnd));
                    existing.UpdateEffectiveWindow(effectiveFromUtc, effectiveUntilUtc);
                    results.Add((existing, false));
                    continue;
                }

                var created = ParcelRouteFare.Create(
                    command.RouteId,
                    item.SizeCategory,
                    command.OperatorId,
                    Money.FromRaw(item.PriceVnd),
                    effectiveFromUtc,
                    effectiveUntilUtc);
                createdRows.Add(created);
                results.Add((created, true));
            }

            if (createdRows.Count > 0)
            {
                await _repository.AddRangeAsync(createdRows, cancellationToken);
            }

            return new BatchParcelRouteFareResponse(
                command.RouteId,
                results.Select(result => new BatchParcelRouteFareItemResponse(
                        result.Fare.SizeCategory.ToString(),
                        result.Fare.PriceVnd.Amount,
                        result.Fare.EffectiveFrom,
                        result.Fare.EffectiveUntil,
                        result.Created))
                    .ToArray());
        }, cancellationToken);
    }

    private static IReadOnlyList<(ParcelSizeCategory SizeCategory, long PriceVnd)> ParseAndValidateItems(
        BatchParcelRouteFareCommand command)
    {
        if (command.Items.Count is < 1 or > 4)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Items must contain between 1 and 4 entries.");
        }

        if (command.EffectiveUntil.HasValue
            && command.EffectiveUntil.Value <= command.EffectiveFrom)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "EffectiveUntil must be after EffectiveFrom.");
        }

        var parsed = new List<(ParcelSizeCategory SizeCategory, long PriceVnd)>(command.Items.Count);
        var seen = new HashSet<ParcelSizeCategory>();
        foreach (var item in command.Items)
        {
            var category = ParseSizeCategory(item.SizeCategory);
            if (!seen.Add(category))
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "Items must contain unique sizeCategory values.");
            }

            if (item.PriceVnd <= 0)
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "PriceVnd must be a positive whole VND amount.");
            }

            parsed.Add((category, item.PriceVnd));
        }

        return parsed;
    }

    private static ParcelSizeCategory ParseSizeCategory(string? value)
    {
        foreach (var category in Enum.GetValues<ParcelSizeCategory>())
        {
            if (string.Equals(category.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        throw new CodedValidationException(
            "INVALID_SIZE_CATEGORY",
            $"'{value}' is not a valid ParcelSizeCategory.");
    }
}
