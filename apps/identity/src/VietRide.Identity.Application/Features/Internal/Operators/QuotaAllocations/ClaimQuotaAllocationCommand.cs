using MediatR;

namespace VietRide.Identity.Application.Features.Internal.Operators.QuotaAllocations;

public sealed record ClaimQuotaAllocationCommand(Guid OperatorId, string Resource, Guid ResourceId, string? PeriodKey) : IRequest<QuotaAllocationDto>;
public sealed record ReleaseQuotaAllocationCommand(Guid OperatorId, Guid AllocationId) : IRequest;
public sealed record QuotaAllocationDto(Guid AllocationId, string Resource, Guid ResourceId, string? PeriodKey);
