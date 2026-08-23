namespace VietRide.Payment.Application.Features.OperatorReports;

public sealed record OperatorLedgerReportRow(
    Guid EntryId,
    string EntryType,
    string ReferenceType,
    Guid ReferenceId,
    Guid? TripId,
    long AmountVnd,
    DateTimeOffset OccurredAt,
    string? Note,
    string? ReferenceCode = null,
    string? TripCode = null);
