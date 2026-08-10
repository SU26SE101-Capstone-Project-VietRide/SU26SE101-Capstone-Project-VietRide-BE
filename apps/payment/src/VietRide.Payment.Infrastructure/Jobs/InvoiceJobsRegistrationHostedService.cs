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
        PaymentRecurringJobRegistration.RegisterInvoiceJobs(_jobs, _options.ReconciliationCron);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
