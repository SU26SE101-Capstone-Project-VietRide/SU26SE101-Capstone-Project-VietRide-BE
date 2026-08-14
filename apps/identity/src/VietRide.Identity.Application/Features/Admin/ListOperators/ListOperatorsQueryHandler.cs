using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Identity.Application.Features.Admin.ListOperators;

public sealed class ListOperatorsQueryHandler : IRequestHandler<ListOperatorsQuery, PagedResult<OperatorListItemDto>>
{
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "contactEmail", "contactPhone", "businessRegistrationNumber", "taxCode",
        "registrationStatus", "isActive", "createdAt", "approvedAt", "suspendedAt",
    };

    private readonly IOperatorRepository _operators;

    public ListOperatorsQueryHandler(IOperatorRepository operators)
    {
        _operators = operators;
    }

    public async Task<PagedResult<OperatorListItemDto>> Handle(
        ListOperatorsQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can list operators.");
        if (!string.IsNullOrWhiteSpace(request.SortBy) && !AllowedSortFields.Contains(request.SortBy))
            throw new BadRequestException("INVALID_SORT_FIELD", "SortBy is not supported.");

        var options = new QueryOptions
        {
            Page = request.Page ?? 1,
            PageSize = request.PageSize ?? 20,
            Search = request.Search,
            SortBy = request.SortBy,
            SortDir = string.IsNullOrWhiteSpace(request.SortDir) ? "desc" : request.SortDir,
        };
        var status = ParseStatus(request.Status);
        var dateField = string.IsNullOrWhiteSpace(request.DateField) ? "createdAt" : request.DateField.Trim();
        DateTimeOffset? fromUtc = request.From.HasValue
            ? BusinessTime.ToUtc(request.From.Value, TimeOnly.MinValue)
            : null;
        DateTimeOffset? toUtcExclusive = request.To.HasValue
            ? BusinessTime.ToUtc(request.To.Value.AddDays(1), TimeOnly.MinValue)
            : null;
        var hasExtendedFilters = request.IsActive.HasValue || request.From.HasValue
            || request.To.HasValue || !string.IsNullOrWhiteSpace(request.DateField);
        var page = hasExtendedFilters
            ? await _operators.ListFilteredAsync(
                options, status, request.IsActive, fromUtc, toUtcExclusive, dateField, cancellationToken)
            : await _operators.ListAsync(options, status, cancellationToken);

        return PagedResult<OperatorListItemDto>.Create(
            page.Items.Select(ToDto).ToArray(),
            page.Page,
            page.PageSize,
            page.TotalItems);
    }

    private static OperatorRegistrationStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        return Enum.Parse<OperatorRegistrationStatus>(status, ignoreCase: true);
    }

    private static OperatorListItemDto ToDto(Operator operatorEntity)
        => new(
            operatorEntity.Id,
            operatorEntity.Name,
            operatorEntity.ContactEmail,
            operatorEntity.ContactPhone,
            operatorEntity.BusinessRegistrationNumber,
            operatorEntity.TaxCode,
            operatorEntity.RegistrationStatus.ToString(),
            operatorEntity.IsActive,
            operatorEntity.CreatedAt,
            operatorEntity.ApprovedAt,
            operatorEntity.SuspendedAt);
}
