using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record ExpireParcelIncidentSearchesCommand(int MaxBatch = 100) : IRequest<int>;
