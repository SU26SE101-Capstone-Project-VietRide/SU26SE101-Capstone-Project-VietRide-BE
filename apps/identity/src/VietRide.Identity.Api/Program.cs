using Serilog;
using VietRide.Identity.Infrastructure;
using VietRide.Shared.Persistence.DependencyInjection;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Health;
using VietRide.Shared.Web.Middleware;
using VietRide.Shared.Web.Swagger;

const string ServiceName = "Identity";

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddVietRideDbContext<IdentityDbContext>(builder.Configuration);

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
