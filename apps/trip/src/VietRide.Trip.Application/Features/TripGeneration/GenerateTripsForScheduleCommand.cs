using MediatR;

namespace VietRide.Trip.Application.Features.TripGeneration;

public sealed record GenerateTripsForScheduleCommand(Guid? DriverScheduleId = null) : IRequest<GenerateTripsForScheduleResult>;

public sealed record GenerateTripsForScheduleResult(int GeneratedCount, int SkippedCount);
