using DotNetEnv;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Serilog;
using VietRide.Identity.Application.Features.Auth.Register;
using VietRide.Identity.Infrastructure;
using VietRide.Identity.Infrastructure.DependencyInjection;
using VietRide.Identity.Infrastructure.Jobs;
using VietRide.Identity.Infrastructure.Seed;
using VietRide.Shared.Application.DependencyInjection;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Persistence.DependencyInjection;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Health;
using VietRide.Shared.Web.Middleware;
using VietRide.Shared.Web.Swagger;

const string ServiceName = "Identity";

Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
var isTesting = builder.Environment.IsEnvironment("Testing");

// Structured logging — overridden via appsettings or env.
builder.Host.UseSerilog((ctx, _, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", ServiceName)
    .WriteTo.Console());

// Shared cross-cutting (Internal JWT auth, Problem+JSON, Swagger, Health, IClock).
builder.Services.AddVietRideSharedWeb(builder.Configuration, ServiceName);

// EF Core (Npgsql) — picks up ConnectionStrings:Default
// or env IDENTITY__CONNECTIONSTRINGS__DEFAULT.
builder.Services.AddVietRideDbContext<IdentityDbContext>(
    builder.Configuration,
    configureDataSource: IdentityDbContext.ConfigurePostgresEnums,
    configureDbContext: isTesting
        ? options => options.ConfigureWarnings(warnings => warnings.Ignore(
            Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
        : null);
builder.Services.AddVietRideIntegrationInbox<IdentityDbContext>();

// MediatR v11 pipeline behaviors (Logging → Validation → Transaction)
// + FluentValidation validators discovered from the Application assembly.
builder.Services.AddVietRideMediatRBehaviors(
    handlerAssemblies: [typeof(RegisterCommand).Assembly]);

// Infrastructure: repositories, security services, email stub, Redis OTP rate-limiter.
builder.Services.AddInfrastructure(builder.Configuration, registerEventConsumer: !isTesting);
builder.Services.AddVietRideIdempotency("identity", requireAllMutations: true);

var registerRecurringJobs = !isTesting;
if (registerRecurringJobs)
{
    builder.Services.AddIdentityHangfire(builder.Configuration);
    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = builder.Configuration.GetValue("Hangfire:WorkerCount", 2);
    });
}

// RabbitMQ publisher + Outbox background drainer (publishes integration events).
// WebApplicationFactory fixtures frequently use placeholder or short-lived databases;
// starting the polling worker there keeps connections alive after fixture teardown.
if (!isTesting)
{
    builder.Services.AddVietRideMessaging(builder.Configuration);
}

var app = builder.Build();

if (!IsWebApplicationFactoryHost())
{
    await using var scope = app.Services.CreateAsyncScope();
    // Apply pending EF Core migrations before seeding (creates the schema on first boot),
    // then reload the Npgsql type catalog so the native enums (user_role, user_status, …)
    // resolve on a fresh DB — otherwise the first enum read fails with DataTypeName '-'.
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().MigrateAndReloadTypesAsync();
    var bootstrapAdminSeeder = scope.ServiceProvider.GetRequiredService<BootstrapAdminSeeder>();
    await bootstrapAdminSeeder.SeedAsync();
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseVietRideSwagger();
app.UseAuthentication();
app.UseAuthorization();
app.UseVietRideIdempotency();
app.MapVietRideHealth(ServiceName);
app.MapControllers();

if (registerRecurringJobs)
{
    using var scope = app.Services.CreateScope();
    var recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    var ict = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
    recurringJobs.AddOrUpdate<SubscriptionLifecycleJob>(
        SubscriptionLifecycleJob.ExpiryJobId,
        job => job.ExpireActiveAsync(CancellationToken.None),
        "30 0 * * *",
        new RecurringJobOptions { TimeZone = ict });
    recurringJobs.AddOrUpdate<SubscriptionLifecycleJob>(
        SubscriptionLifecycleJob.WarningJobId,
        job => job.SendWarningsAsync(CancellationToken.None),
        "0 * * * *",
        new RecurringJobOptions { TimeZone = ict });
    recurringJobs.AddOrUpdate<SubscriptionLifecycleJob>(
        SubscriptionLifecycleJob.RevertJobId,
        job => job.AutoRevertAsync(CancellationToken.None),
        Cron.Minutely(),
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    recurringJobs.AddOrUpdate<SubscriptionLifecycleJob>(
        SubscriptionLifecycleJob.MonthlyResetJobId,
        job => job.ResetMonthlyTripUsageAsync(CancellationToken.None),
        "1 0 1 * *",
        new RecurringJobOptions { TimeZone = ict });
    recurringJobs.AddOrUpdate<OperatorWalletBackfillJob>(
        OperatorWalletBackfillJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        Cron.Minutely(),
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}

app.Run();

static bool IsWebApplicationFactoryHost()
    => AppDomain.CurrentDomain.GetAssemblies()
        .Any(assembly => assembly.GetName().Name == "Microsoft.AspNetCore.Mvc.Testing");

// Expose Program for WebApplicationFactory<Program> in integration tests.
public partial class Program;
