using MediatR;

namespace VietRide.Trip.Application.Features.Stations;

public sealed record GetAdminStationSummaryQuery : IRequest<AdminStationSummaryDto>;
