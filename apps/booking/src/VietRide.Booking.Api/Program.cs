using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;
using VietRide.Booking.Application;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.Infrastructure.DependencyInjection;
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
    configureDataSource: BookingDbContext.ConfigurePostgresTypes);
builder.Services.AddVietRideMediatRBehaviors(
    handlerAssemblies: [typeof(ApplicationAssemblyMarker).Assembly]);
var registerMessaging = !builder.Environment.IsEnvironment("Testing");
if (registerMessaging)
{
    builder.Services.AddVietRideMessaging(builder.Configuration);
}

builder.Services.AddInfrastructure(builder.Configuration, registerConsumers: registerMessaging);
builder.Services.AddVietRideIdempotency("booking");

var app = builder.Build();

if (!IsWebApplicationFactoryHost())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    await dbContext.Database.MigrateAsync();

    // On a fresh database the shared NpgsqlDataSource caches the PG type catalog on its first
    // connection — which is the MigrateAsync above, opened BEFORE the migration creates the enum
    // types (voucher_type, voucher_funding_type, operator_voucher_consent_status, booking_status, …).
    // Without a reload, every subsequent enum parameter write fails at runtime with
    // "Cannot resolve '<enum>' to a fully qualified datatype name" until the process is restarted.
    // Reload the catalog now so the mapped enums resolve on first boot against an empty DB.
    var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
    var wasClosed = connection.State != ConnectionState.Open;
    if (wasClosed)
    {
        await connection.OpenAsync();
    }

    await connection.ReloadTypesAsync();

    if (wasClosed)
    {
        await connection.CloseAsync();
    }
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

// Expose Program for WebApplicationFactory<Program> in integration tests.
public partial class Program;
