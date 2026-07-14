using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VietRide.Payment.Infrastructure.Invoices;

namespace VietRide.Payment.Infrastructure.Jobs;

internal sealed class InvoiceJobsRegistrationHostedService : IHostedService
{
    private readonly IRecurringJobManager _jobs;
    private readonly InvoicePdfOptions _options;

    public InvoiceJobsRegistrationHostedService(
        IRecurringJobManager jobs,
        IOptions<InvoicePdfOptions> options)
    {
        _jobs = jobs;
        _options = options.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _jobs.AddOrUpdate<InvoicePdfReconciliationJob>(
            InvoicePdfReconciliationJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            _options.ReconciliationCron);
        _jobs.AddOrUpdate<Day38InvoiceBackfillJob>(
            Day38InvoiceBackfillJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            "*/10 * * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
