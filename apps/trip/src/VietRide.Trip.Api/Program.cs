using Microsoft.EntityFrameworkCore;
using Serilog;
using VietRide.Shared.Application.DependencyInjection;
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
    configureDataSource: TripDbContext.ConfigurePostgresEnums);
builder.Services.AddVietRideMediatRBehaviors(
    handlerAssemblies: [typeof(ApplicationAssemblyMarker).Assembly]);
builder.Services.AddInfrastructure(builder.Configuration);
var app = builder.Build();

if (!IsWebApplicationFactoryHost())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<TripDbContext>().Database.MigrateAsync();
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseVietRideSwagger();
app.UseAuthentication();
app.UseAuthorization();
app.MapVietRideHealth(ServiceName);
app.MapControllers();

app.Run();

static bool IsWebApplicationFactoryHost()
    => AppDomain.CurrentDomain.GetAssemblies()
        .Any(assembly => assembly.GetName().Name == "Microsoft.AspNetCore.Mvc.Testing");

// Expose Program for WebApplicationFactory<Program> in integration tests.
public partial class Program;
