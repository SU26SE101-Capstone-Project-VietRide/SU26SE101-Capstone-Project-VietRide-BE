using System.Globalization;
using System.Text;
using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Identity.Application.Features.Admin.ExportOperators;

public sealed class ExportOperatorsQueryHandler(IOperatorRepository operators, IClock? clock = null)
    : IRequestHandler<ExportOperatorsQuery, ExportOperatorsResult>
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "contactEmail", "contactPhone", "businessRegistrationNumber", "taxCode",
        "registrationStatus", "isActive", "createdAt", "approvedAt", "suspendedAt",
    };

    public async Task<ExportOperatorsResult> Handle(
        ExportOperatorsQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can export operators.");
        if (!string.IsNullOrWhiteSpace(request.SortBy) && !SortFields.Contains(request.SortBy))
            throw new BadRequestException("INVALID_SORT_FIELD", "SortBy is not supported.");
        if (request.From.HasValue && request.To.HasValue && request.From > request.To)
            throw new CodedValidationException("VALIDATION_ERROR", "from must be on or before to.");

        var status = string.IsNullOrWhiteSpace(request.Status)
            ? (OperatorRegistrationStatus?)null
            : Enum.Parse<OperatorRegistrationStatus>(request.Status, true);
        var dateField = string.IsNullOrWhiteSpace(request.DateField) ? "createdAt" : request.DateField.Trim();
        var fromUtc = request.From.HasValue
            ? BusinessTime.ToUtc(request.From.Value, TimeOnly.MinValue)
            : (DateTimeOffset?)null;
        var toUtc = request.To.HasValue
            ? BusinessTime.ToUtc(request.To.Value.AddDays(1), TimeOnly.MinValue)
            : (DateTimeOffset?)null;
        var rows = await operators.ListForExportAsync(
            new QueryOptions
            {
                Search = request.Search,
                SortBy = request.SortBy,
                SortDir = string.IsNullOrWhiteSpace(request.SortDir) ? "desc" : request.SortDir,
            },
            status,
            request.IsActive,
            fromUtc,
            toUtc,
            dateField,
            cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine("Tên nhà xe,Email liên hệ,Số điện thoại liên hệ,Số đăng ký kinh doanh,Mã số thuế,Trạng thái đăng ký,Đang hoạt động,Ngày tạo,Ngày duyệt,Ngày tạm ngưng,Mã hệ thống");
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                Escape(row.Name), Escape(row.ContactEmail), Escape(row.ContactPhone),
                Escape(row.BusinessRegistrationNumber), Escape(row.TaxCode),
                Escape(OperatorExportLabels.RegistrationStatus(row.RegistrationStatus)), row.IsActive ? "Có" : "Không",
                Escape(FormatDateTime(row.CreatedAt)),
                Escape(row.ApprovedAt.HasValue ? FormatDateTime(row.ApprovedAt.Value) : string.Empty),
                Escape(row.SuspendedAt.HasValue ? FormatDateTime(row.SuspendedAt.Value) : string.Empty),
                Escape(row.Id.ToString("D")),
            }));
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(csv.ToString());
        var content = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, content, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, content, preamble.Length, body.Length);
        return new ExportOperatorsResult(
            content,
            "text/csv; charset=utf-8",
            $"danh-sach-nha-xe-{BusinessTime.ToLocalDate((clock ?? new SystemClock()).UtcNow):yyyyMMdd}.csv");
    }

    private static string FormatDateTime(DateTimeOffset value)
        => BusinessTime.ToLocalDateTime(value).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Escape(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;
        return safe.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{safe.Replace("\"", "\"\"")}\""
            : safe;
    }
}
