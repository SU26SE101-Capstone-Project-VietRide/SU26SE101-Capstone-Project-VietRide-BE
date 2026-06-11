using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Application.Features.Admin.ListOperators;

public sealed class ListOperatorsQueryHandler : IRequestHandler<ListOperatorsQuery, PagedResult<OperatorListItemDto>>
{
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

        var options = new QueryOptions
        {
            Page = request.Page ?? 1,
            PageSize = request.PageSize ?? 20,
            Search = request.Search,
            SortBy = request.SortBy,
            SortDir = string.IsNullOrWhiteSpace(request.SortDir) ? "desc" : request.SortDir,
        };
        var status = ParseStatus(request.Status);
        var page = await _operators.ListAsync(options, status, cancellationToken);

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
