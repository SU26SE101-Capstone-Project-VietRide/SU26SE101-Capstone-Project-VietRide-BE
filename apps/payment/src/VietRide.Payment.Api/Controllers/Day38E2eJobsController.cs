using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Infrastructure.Jobs;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Payment.Api.Controllers;

/// <summary>Explicitly gated trigger surface for the isolated Day 38 acceptance stack.</summary>
[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/e2e/day38/jobs")]
public sealed class Day38E2eJobsController : ControllerBase
{
    private readonly IBackgroundJobClient _jobs;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public Day38E2eJobsController(
        IBackgroundJobClient jobs,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _jobs = jobs;
        _environment = environment;
        _configuration = configuration;
    }

    [HttpPost("{jobName}")]
    public IActionResult Enqueue(string jobName, [FromQuery] Guid? invoiceId = null)
    {
        if (!_environment.IsDevelopment()
            || !string.Equals(_configuration["Day38E2E:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var jobId = jobName switch
        {
            "context-backfill" => _jobs.Enqueue<Day38PaymentContextBackfillJob>(
                job => job.RunAsync(CancellationToken.None)),
            "revenue-backfill" => _jobs.Enqueue<Day38RevenueLedgerBackfillJob>(
                job => job.RunAsync(CancellationToken.None)),
            "invoice-backfill" => _jobs.Enqueue<Day38InvoiceBackfillJob>(
                job => job.RunAsync(CancellationToken.None)),
            "invoice-reconciliation" => _jobs.Enqueue<InvoicePdfReconciliationJob>(
                job => job.RunAsync(CancellationToken.None)),
            "invoice-pdf" when invoiceId.HasValue => _jobs.Enqueue<InvoicePdfGenerationJob>(
                job => job.RunAsync(invoiceId.Value, CancellationToken.None)),
            "settlement-eligibility" => _jobs.Enqueue<TripSettlementEligibilityFlagJob>(
                job => job.RunAsync(CancellationToken.None)),
            "settlement-weekly" => _jobs.Enqueue<TripSettlementWeeklyAutoSettleJob>(
                job => job.RunAsync(CancellationToken.None)),
            "settlement-alert" => _jobs.Enqueue<TripSettlementStuckAlertJob>(
                job => job.RunAsync(CancellationToken.None)),
            _ => string.Empty,
        };

        return string.IsNullOrEmpty(jobId)
            ? BadRequest(new { errorCode = "DAY38_E2E_JOB_INVALID" })
            : Accepted(new { jobId });
    }
}
