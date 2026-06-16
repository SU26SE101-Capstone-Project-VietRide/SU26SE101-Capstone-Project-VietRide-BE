using Hangfire;
using Microsoft.EntityFrameworkCore;
using Serilog;
using VietRide.Payment.Application;
using VietRide.Payment.Infrastructure;
using VietRide.Payment.Infrastructure.DependencyInjection;
using VietRide.Payment.Infrastructure.Jobs;
using VietRide.Shared.Application.DependencyInjection;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Persistence.DependencyInjection;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Health;
using VietRide.Shared.Web.Middleware;
using VietRide.Shared.Web.Swagger;

const string ServiceName = "Payment";

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, _, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", ServiceName)
    .WriteTo.Console());

builder.Services.AddVietRideSharedWeb(builder.Configuration, ServiceName);
builder.Services.AddVietRideDbContext<PaymentDbContext>(
    builder.Configuration,
    configureDataSource: PaymentDbContext.ConfigurePostgresTypes);
builder.Services.AddVietRideMediatRBehaviors(
    handlerAssemblies: [typeof(ApplicationAssemblyMarker).Assembly]);
var registerMessaging = !builder.Environment.IsEnvironment("Testing");
if (registerMessaging)
{
    builder.Services.AddVietRideMessaging(builder.Configuration);
    builder.Services.AddPaymentHangfire(builder.Configuration);
    builder.Services.AddHangfireServer();
}

builder.Services.AddInfrastructure(builder.Configuration, registerConsumers: registerMessaging);
builder.Services.AddVietRideIdempotency("payment");

var app = builder.Build();

if (!IsWebApplicationFactoryHost())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
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
    recurringJobs.AddOrUpdate<TopUpExpiredJob>(
        TopUpExpiredJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        Cron.Minutely());
}

app.Run();

static bool IsWebApplicationFactoryHost()
    => AppDomain.CurrentDomain.GetAssemblies()
        .Any(assembly => assembly.GetName().Name == "Microsoft.AspNetCore.Mvc.Testing");

// Expose Program for WebApplicationFactory<Program> in integration tests.
public partial class Program;
