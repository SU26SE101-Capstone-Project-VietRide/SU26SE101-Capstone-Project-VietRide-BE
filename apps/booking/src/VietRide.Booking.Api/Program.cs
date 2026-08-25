using Hangfire;
using Hangfire.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;
using VietRide.Booking.Application;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.Infrastructure.DependencyInjection;
using VietRide.Booking.Infrastructure.Jobs;
using VietRide.Shared.Application.DependencyInjection;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Persistence.DependencyInjection;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Health;
using VietRide.Shared.Web.Middleware;
using VietRide.Shared.Web.Swagger;

const string ServiceName = "Booking";

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, _, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", ServiceName)
    .WriteTo.Console());

builder.Services.AddVietRideSharedWeb(builder.Configuration, ServiceName);
builder.Services.AddVietRideDbContext<BookingDbContext>(
    builder.Configuration,
    configureDataSource: BookingDbContext.ConfigurePostgresTypes,
    configureDbContext: options =>
    {
        if (builder.Environment.IsEnvironment("Testing"))
        {
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        }
    });
builder.Services.AddVietRideIntegrationInbox<BookingDbContext>();
builder.Services.AddVietRideMediatRBehaviors(
    handlerAssemblies: [typeof(ApplicationAssemblyMarker).Assembly]);
var registerMessaging = !builder.Environment.IsEnvironment("Testing");
if (registerMessaging)
{
    builder.Services.AddVietRideMessaging(builder.Configuration);
}

builder.Services.AddInfrastructure(builder.Configuration, registerConsumers: registerMessaging);
if (registerMessaging)
{
    builder.Services.AddBookingHangfire(builder.Configuration);
}
builder.Services.AddVietRideIdempotency("booking", requireAllMutations: true);

var app = builder.Build();

if (!IsWebApplicationFactoryHost())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    // Migrate, then reload the Npgsql type catalog so the native enums (voucher_type,
    // booking_status, …) resolve on a fresh DB — otherwise the first enum read/write fails
    // at runtime with DataTypeName '-'. See MigrateAndReloadTypesAsync for the full rationale.
    await dbContext.MigrateAndReloadTypesAsync();
}

if (registerMessaging)
{
    app.Services.GetRequiredService<IStopDisabledAutoFallbackScheduler>().EnsureScheduled();
    app.Services.GetRequiredService<INoShowDetectionScheduler>().EnsureScheduled();
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseVietRideSwagger();
app.UseAuthentication();
app.UseAuthorization();
app.UseVietRideIdempotency();
app.MapVietRideHealth(ServiceName);
app.MapControllers();

if (registerMessaging)
{
    using var scope = app.Services.CreateScope();
    var recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    var hangfireQueueName = VietRide.Booking.Infrastructure.Jobs.HangfireServiceCollectionExtensions
        .GetQueueName(builder.Configuration);
#pragma warning disable CS0618
    recurringJobs.AddOrUpdate(
        PlatformBookingStatsBackfillJob.RecurringJobId,
        Job.FromExpression<PlatformBookingStatsBackfillJob>(job =>
            job.RunAsync(CancellationToken.None)),
        "*/5 * * * *",
        new RecurringJobOptions { QueueName = hangfireQueueName, TimeZone = TimeZoneInfo.Utc });
    recurringJobs.AddOrUpdate(
        BuyerSnapshotBackfillJob.RecurringJobId,
        Job.FromExpression<BuyerSnapshotBackfillJob>(job =>
            job.RunAsync(CancellationToken.None)),
        "*/5 * * * *",
        new RecurringJobOptions { QueueName = hangfireQueueName, TimeZone = TimeZoneInfo.Utc });
    recurringJobs.AddOrUpdate(
        BookingTransferEscalationJob.RecurringJobId,
        Job.FromExpression<BookingTransferEscalationJob>(job =>
            job.RunAsync(CancellationToken.None)),
        "*/5 * * * *",
        new RecurringJobOptions { QueueName = hangfireQueueName, TimeZone = TimeZoneInfo.Utc });
#pragma warning restore CS0618
}

app.Run();

static bool IsWebApplicationFactoryHost()
    => AppDomain.CurrentDomain.GetAssemblies()
        .Any(assembly => assembly.GetName().Name == "Microsoft.AspNetCore.Mvc.Testing");

// Expose Program for WebApplicationFactory<Program> in integration tests.
public partial class Program;
