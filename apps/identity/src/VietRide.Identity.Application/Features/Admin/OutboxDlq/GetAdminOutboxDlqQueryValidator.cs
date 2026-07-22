using System.Text.Json;
using FluentValidation;

namespace VietRide.Identity.Application.Features.Admin.OutboxDlq;

public sealed class GetAdminOutboxDlqQueryValidator : AbstractValidator<GetAdminOutboxDlqQuery>
{
    private static readonly string[] Services =
        ["identity", "trip", "booking", "payment", "parcel", "tracking"];

    public GetAdminOutboxDlqQueryValidator()
    {
        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(query => query.Service)
            .Must(service => service is null || Services.Contains(service, StringComparer.OrdinalIgnoreCase))
            .WithMessage("service must be one of identity, trip, booking, payment, parcel, tracking.");

        RuleFor(query => query.EventType)
            .MaximumLength(100)
            .When(query => query.EventType is not null);

        RuleFor(query => query.SortDir)
            .Must(value => string.Equals(value, "asc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("sortDir must be asc or desc.");

        RuleFor(query => query.Cursor)
            .MaximumLength(512)
            .Must(IsValidCursor)
            .WithMessage("cursor must be a valid opaque DLQ cursor.")
            .When(query => query.Cursor is not null);
    }

    private static bool IsValidCursor(string? value)
    {
        if (value is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
        {
            return false;
        }

        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(
                normalized.Length + ((4 - normalized.Length % 4) % 4),
                '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(normalized));
            var cursor = document.RootElement;
            return cursor.ValueKind == JsonValueKind.Object
                && cursor.TryGetProperty("Service", out var service)
                && service.ValueKind == JsonValueKind.String
                && Services.Contains(service.GetString()!, StringComparer.OrdinalIgnoreCase)
                && cursor.TryGetProperty("TerminalAt", out var terminalAt)
                && terminalAt.TryGetDateTimeOffset(out var timestamp)
                && timestamp != default
                && cursor.TryGetProperty("EventId", out var id)
                && id.TryGetGuid(out var eventId)
                && eventId != Guid.Empty;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }
}
