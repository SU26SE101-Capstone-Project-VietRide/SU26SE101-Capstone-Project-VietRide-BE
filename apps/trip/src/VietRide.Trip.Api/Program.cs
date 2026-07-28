using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;
using VietRide.Shared.Application.DependencyInjection;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Persistence.DependencyInjection;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Health;
using VietRide.Shared.Web.Middleware;
using VietRide.Shared.Web.Swagger;
using VietRide.Trip.Application;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.Infrastructure.DependencyInjection;

const string ServiceName = "Trip";

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, _, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", ServiceName)
    .WriteTo.Console());

builder.Services.AddVietRideSharedWeb(builder.Configuration, ServiceName);
builder.Services.AddVietRideDbContext<TripDbContext>(
    builder.Configuration,
    configureDataSource: TripDbContext.ConfigurePostgresEnums,
    configureDbContext: options =>
    {
        if (builder.Environment.IsEnvironment("Testing"))
        {
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        }
    });
builder.Services.AddVietRideIntegrationInbox<TripDbContext>();
var backgroundWorkersEnabled = AreBackgroundWorkersEnabled(
    builder.Configuration,
    builder.Environment);
if (backgroundWorkersEnabled)
{
    builder.Services.AddVietRideMessaging(builder.Configuration);
}
builder.Services.AddVietRideMediatRBehaviors(
    handlerAssemblies: [typeof(ApplicationAssemblyMarker).Assembly]);
builder.Services.AddInfrastructure(builder.Configuration, backgroundWorkersEnabled);
builder.Services.AddVietRideIdempotency("trip", requireAllMutations: true);
var app = builder.Build();

if (!IsWebApplicationFactoryHost())
{
    await using var scope = app.Services.CreateAsyncScope();
    // Migrate, then reload the Npgsql type catalog so the native enums resolve on a fresh
    // DB — otherwise the first enum read fails at runtime with DataTypeName '-'.
    await scope.ServiceProvider.GetRequiredService<TripDbContext>().MigrateAndReloadTypesAsync();
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseVietRideSwagger();
app.UseAuthentication();
app.UseAuthorization();
app.UseVietRideIdempotency();
app.MapVietRideHealth(ServiceName);
app.MapControllers();

app.Run();

static bool IsWebApplicationFactoryHost()
    => AppDomain.CurrentDomain.GetAssemblies()
        .Any(assembly => assembly.GetName().Name == "Microsoft.AspNetCore.Mvc.Testing");

static bool AreBackgroundWorkersEnabled(
    IConfiguration configuration,
    IHostEnvironment environment) =>
    !environment.IsEnvironment("Testing") &&
    (configuration.GetValue<bool?>("Trip:BackgroundWorkers:Enabled") ?? true);

// Expose Program for WebApplicationFactory<Program> in integration tests.
public partial class Program;
