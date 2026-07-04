using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Serilog;
using VietRide.Identity.Application.Features.Auth.Register;
using VietRide.Identity.Infrastructure;
using VietRide.Identity.Infrastructure.DependencyInjection;
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
    configureDataSource: IdentityDbContext.ConfigurePostgresEnums);

// MediatR v11 pipeline behaviors (Logging → Validation → Transaction)
// + FluentValidation validators discovered from the Application assembly.
builder.Services.AddVietRideMediatRBehaviors(
    handlerAssemblies: [typeof(RegisterCommand).Assembly]);

// Infrastructure: repositories, security services, email stub, Redis OTP rate-limiter.
builder.Services.AddInfrastructure(builder.Configuration);

// RabbitMQ publisher + Outbox background drainer (publishes integration events).
builder.Services.AddVietRideMessaging(builder.Configuration);

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
app.MapVietRideHealth(ServiceName);
app.MapControllers();

app.Run();

static bool IsWebApplicationFactoryHost()
    => AppDomain.CurrentDomain.GetAssemblies()
        .Any(assembly => assembly.GetName().Name == "Microsoft.AspNetCore.Mvc.Testing");

// Expose Program for WebApplicationFactory<Program> in integration tests.
public partial class Program;
