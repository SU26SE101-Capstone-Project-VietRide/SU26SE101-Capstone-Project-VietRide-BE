using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Features.Settlements.HandleTripTerminal;

public sealed class TripTerminalSettlementService
{
    private const string ConsumerName = "payment.trip-terminal-settlement";
    private readonly IOperatorTripSettlementRepository _settlements;
    private readonly IOperatorLedgerEntryRepository _ledger;
    private readonly IProcessedIntegrationEventRepository _processed;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public TripTerminalSettlementService(
        IOperatorTripSettlementRepository settlements,
        IOperatorLedgerEntryRepository ledger,
        IProcessedIntegrationEventRepository processed,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _settlements = settlements;
        _ledger = ledger;
        _processed = processed;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task HandleAsync(
        Guid eventId,
        Guid operatorId,
        Guid tripId,
        DateTimeOffset terminalAt,
        CancellationToken cancellationToken,
        string? tripCode = null)
    {
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (await _processed.ExistsAsync(ConsumerName, eventId, cancellationToken))
                return false;

            var settlement = await _settlements.FindByOperatorTripAsync(
                operatorId,
                tripId,
                cancellationToken);
            if (settlement is null)
            {
                settlement = OperatorTripSettlement.CreatePending(operatorId, tripId, terminalAt, tripCode);
                var netAmount = await _ledger.SumTripNetAmountAsync(
                    operatorId,
                    tripId,
                    cancellationToken);
                settlement.RefreshEligibility(netAmount, _clock.UtcNow);
                await _settlements.AddAsync(settlement, cancellationToken);
            }
            else if (tripCode is not null)
            {
                settlement.SetTripCode(tripCode);
            }

            await _processed.AddAsync(
                ProcessedIntegrationEvent.Create(ConsumerName, eventId, _clock.UtcNow),
                cancellationToken);
            return true;
        }, cancellationToken);
    }
}

public sealed class TripCompletedSettlementEventHandler
    : IIntegrationEventHandler<TripCompletedConsumerEvent>
{
    private readonly TripTerminalSettlementService _service;
    public TripCompletedSettlementEventHandler(TripTerminalSettlementService service) => _service = service;

    public Task HandleAsync(TripCompletedConsumerEvent integrationEvent, CancellationToken cancellationToken)
        => _service.HandleAsync(
            integrationEvent.EventId,
            integrationEvent.OperatorId,
            integrationEvent.TripId,
            integrationEvent.TerminalAt,
            cancellationToken,
            integrationEvent.TripCode);
}

public sealed class TripDisruptedSettlementEventHandler
    : IIntegrationEventHandler<TripDisruptedConsumerEvent>
{
    private readonly TripTerminalSettlementService _service;
    public TripDisruptedSettlementEventHandler(TripTerminalSettlementService service) => _service = service;

    public Task HandleAsync(TripDisruptedConsumerEvent integrationEvent, CancellationToken cancellationToken)
        => _service.HandleAsync(
            integrationEvent.EventId,
            integrationEvent.OperatorId,
            integrationEvent.TripId,
            integrationEvent.TerminalAt,
            cancellationToken,
            integrationEvent.TripCode);
}
