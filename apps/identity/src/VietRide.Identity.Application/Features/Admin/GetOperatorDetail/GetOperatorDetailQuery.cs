using MediatR;

namespace VietRide.Identity.Application.Features.Admin.GetOperatorDetail;

public sealed record GetOperatorDetailQuery(
    string CallerRole,
    Guid OperatorId) : IRequest<AdminOperatorDetailDto>;
