using MediatR;
using VietRide.Payment.Application.Abstractions.Repositories;

namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed class GetPlatformLedgerReportQueryHandler
    : IRequestHandler<GetPlatformLedgerReportQuery, PlatformLedgerReportResult>
{
    private readonly IOperatorLedgerEntryRepository _ledger;

    public GetPlatformLedgerReportQueryHandler(IOperatorLedgerEntryRepository ledger)
    {
        _ledger = ledger;
    }

    public async Task<PlatformLedgerReportResult> Handle(
        GetPlatformLedgerReportQuery request,
        CancellationToken ct)
    {
        var range = PlatformReportUtcRange.Parse(request.From, request.To);
        var rows = await _ledger.GetPlatformLedgerMetricsAsync(range.From, range.To, ct)
            .ConfigureAwait(false);
        return new PlatformLedgerReportResult(rows);
    }
}
