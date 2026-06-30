using System.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;
using VietRide.Parcel.Application;
using VietRide.Parcel.Infrastructure;
using VietRide.Parcel.Infrastructure.DependencyInjection;
using VietRide.Parcel.Infrastructure.Jobs;
using VietRide.Shared.Application.DependencyInjection;
using VietRide.Shared.Messaging.DependencyInjection;
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
builder.Services.AddVietRideDbContext<ParcelDbContext>(
    builder.Configuration,
    configureDataSource: ParcelDbContext.ConfigurePostgresTypes);
builder.Services.AddVietRideMediatRBehaviors(
    handlerAssemblies: [typeof(ApplicationAssemblyMarker).Assembly]);
var registerMessaging = !builder.Environment.IsEnvironment("Testing");
if (registerMessaging)
{
    builder.Services.AddVietRideMessaging(builder.Configuration);
    builder.Services.AddParcelHangfire(builder.Configuration);
    builder.Services.AddHangfireServer();
}

builder.Services.AddInfrastructure(builder.Configuration, registerConsumers: registerMessaging);
builder.Services.AddVietRideIdempotency("parcel");

var app = builder.Build();

if (!IsWebApplicationFactoryHost())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
    await dbContext.Database.MigrateAsync();

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

if (registerMessaging)
{
    using var scope = app.Services.CreateScope();
    var recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobs.AddOrUpdate<ParcelReviewTimeoutJob>(
        ParcelReviewTimeoutJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        "*/5 * * * *");
    recurringJobs.AddOrUpdate<ParcelAdditionalPaymentTimeoutJob>(
        ParcelAdditionalPaymentTimeoutJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        "*/5 * * * *");
    recurringJobs.AddOrUpdate<ParcelPendingAutoRejectJob>(
        ParcelPendingAutoRejectJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        "*/5 * * * *");
}

app.Run();

static bool IsWebApplicationFactoryHost()
    => AppDomain.CurrentDomain.GetAssemblies()
        .Any(assembly => assembly.GetName().Name == "Microsoft.AspNetCore.Mvc.Testing");

public partial class Program;
