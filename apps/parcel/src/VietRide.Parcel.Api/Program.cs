using Serilog;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Persistence.DependencyInjection;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Health;
using VietRide.Shared.Web.Middleware;
using VietRide.Shared.Web.Swagger;

const string ServiceName = "Parcel";

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, _, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", ServiceName)
    .WriteTo.Console());

builder.Services.AddVietRideSharedWeb(builder.Configuration, ServiceName);
builder.Services.AddVietRideDbContext<ParcelDbContext>(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseVietRideSwagger();
app.UseAuthentication();
app.UseAuthorization();
app.MapVietRideHealth(ServiceName);
app.MapControllers();

app.Run();

// Expose Program for WebApplicationFactory<Program> in integration tests.
public partial class Program;
