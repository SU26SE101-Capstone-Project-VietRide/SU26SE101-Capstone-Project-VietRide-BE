using MediatR;

namespace VietRide.Identity.Application.Features.Admin.ExportOperators;

public sealed record ExportOperatorsQuery(
    string CallerRole,
    string? Search,
    string? SortBy,
    string? SortDir,
    string? Status,
    bool? IsActive,
    DateOnly? From,
    DateOnly? To,
    string? DateField) : IRequest<ExportOperatorsResult>;

public sealed record ExportOperatorsResult(byte[] Content, string ContentType, string FileName);
