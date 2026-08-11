using MediatR;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.TripGeneration;

public sealed class GenerateTripsForScheduleHandler : IRequestHandler<GenerateTripsForScheduleCommand, GenerateTripsForScheduleResult>
{
    private readonly IDriverScheduleRepository driverScheduleRepository;
    private readonly IClock clock;
    private readonly IRouteRepository routeRepository;
    private readonly IRouteStopFareTemplateRepository routeStopFareTemplateRepository;
    private readonly IRouteStopRepository routeStopRepository;
    private readonly ITripGenerationSkipLogRepository skipLogRepository;
    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly ITripStopFareRepository tripStopFareRepository;
    private readonly ITripStopRepository tripStopRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly IVehicleRepository vehicleRepository;
    private readonly IStationRepository? stationRepository;
    private readonly IStopRepository? stopRepository;
    private readonly ITripEtaPlanner? tripEtaPlanner;
    private readonly IIntegrationEventOutbox outbox;
    private readonly ISubscriptionQuotaClient? quotaClient;
    private readonly IResourceAvailabilityService resourceAvailability;

    public GenerateTripsForScheduleHandler(
        IClock clock,
        IDriverScheduleRepository driverScheduleRepository,
        IRouteRepository routeRepository,
        IRouteStopRepository routeStopRepository,
        IRouteStopFareTemplateRepository routeStopFareTemplateRepository,
        IVehicleRepository vehicleRepository,
        ITripRepository tripRepository,
        ITripSeatRepository tripSeatRepository,
        ITripStopRepository tripStopRepository,
        ITripStopFareRepository tripStopFareRepository,
        ITripGenerationSkipLogRepository skipLogRepository,
        IUnitOfWork unitOfWork,
        IIntegrationEventOutbox outbox,
        IResourceAvailabilityService resourceAvailability,
        ISubscriptionQuotaClient? quotaClient = null,
        IStationRepository? stationRepository = null,
        IStopRepository? stopRepository = null,
        ITripEtaPlanner? tripEtaPlanner = null)
    {
        this.clock = clock;
        this.driverScheduleRepository = driverScheduleRepository;
        this.routeRepository = routeRepository;
        this.routeStopRepository = routeStopRepository;
        this.routeStopFareTemplateRepository = routeStopFareTemplateRepository;
        this.vehicleRepository = vehicleRepository;
        this.tripRepository = tripRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.tripStopRepository = tripStopRepository;
        this.tripStopFareRepository = tripStopFareRepository;
        this.skipLogRepository = skipLogRepository;
        this.unitOfWork = unitOfWork;
        this.outbox = outbox;
        this.resourceAvailability = resourceAvailability;
        this.stationRepository = stationRepository;
        this.stopRepository = stopRepository;
        this.tripEtaPlanner = tripEtaPlanner;
        this.quotaClient = quotaClient;
    }

    public async Task<GenerateTripsForScheduleResult> Handle(
        GenerateTripsForScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var generationService = new TripGenerationService(
            clock,
            driverScheduleRepository,
            routeRepository,
            routeStopRepository,
            routeStopFareTemplateRepository,
            vehicleRepository,
            tripRepository,
            tripSeatRepository,
            tripStopRepository,
            tripStopFareRepository,
            skipLogRepository,
            outbox,
            quotaClient,
            stationRepository,
            stopRepository,
            tripEtaPlanner,
            resourceAvailability);

        try
        {
            var result = await generationService.GenerateAsync(request.DriverScheduleId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch
        {
            await generationService.ReleasePersistedQuotaAllocationsAsync(cancellationToken);
            throw;
        }
    }
}
