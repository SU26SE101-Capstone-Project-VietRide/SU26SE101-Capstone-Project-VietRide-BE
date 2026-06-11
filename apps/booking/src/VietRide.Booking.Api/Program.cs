using Serilog;
using VietRide.Booking.Application;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.Infrastructure.DependencyInjection;
using VietRide.Shared.Application.DependencyInjection;
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
builder.Services.AddVietRideDbContext<BookingDbContext>(builder.Configuration);
builder.Services.AddVietRideMediatRBehaviors(
    handlerAssemblies: [typeof(ApplicationAssemblyMarker).Assembly]);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddVietRideIdempotency("booking");

var app = builder.Build();

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseVietRideSwagger();
app.UseAuthentication();
app.UseAuthorization();
app.UseVietRideIdempotency();
app.MapVietRideHealth(ServiceName);
app.MapControllers();

app.Run();

// Expose Program for WebApplicationFactory<Program> in integration tests.
public partial class Program;
