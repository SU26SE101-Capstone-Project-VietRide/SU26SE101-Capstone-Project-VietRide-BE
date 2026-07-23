using System.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;
using VietRide.Parcel.Application;
using VietRide.Parcel.Application.Features.Parcels.Create;
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
builder.Services.AddSingleton(new ParcelImageOptions(
    builder.Configuration["FIREBASE_WEB_STORAGE_BUCKET"]
        ?? builder.Configuration["FIREBASE_STORAGE_BUCKET"]
        ?? Environment.GetEnvironmentVariable("FIREBASE_STORAGE_BUCKET")));
builder.Services.AddVietRideIntegrationInbox<ParcelDbContext>();
builder.Services.AddVietRideMediatRBehaviors(
    handlerAssemblies: [typeof(ApplicationAssemblyMarker).Assembly]);
var registerMessaging = !builder.Environment.IsEnvironment("Testing");
if (registerMessaging)
{
    builder.Services.AddVietRideMessaging(builder.Configuration);
    builder.Services.AddParcelHangfire(builder.Configuration);
    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = builder.Configuration.GetValue("Hangfire:WorkerCount", 2);
    });
}

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment, registerConsumers: registerMessaging);
builder.Services.AddVietRideIdempotency("parcel", requireAllMutations: true);

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
    var vietnamTimeZone = ResolveVietnamTimeZone();
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
    recurringJobs.AddOrUpdate<ParcelLifecycleSweepJob>(
        ParcelLifecycleSweepJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        "*/5 * * * *");
    recurringJobs.AddOrUpdate<PlatformParcelStatsBackfillJob>(
        PlatformParcelStatsBackfillJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        "*/5 * * * *");
    recurringJobs.AddOrUpdate<ParcelDeliveryPendingConfirmReminderJob>(
        ParcelDeliveryPendingConfirmReminderJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        "0 9 * * *",
        new RecurringJobOptions { TimeZone = vietnamTimeZone });
}

app.Run();

static bool IsWebApplicationFactoryHost()
    => AppDomain.CurrentDomain.GetAssemblies()
        .Any(assembly => assembly.GetName().Name == "Microsoft.AspNetCore.Mvc.Testing");

static TimeZoneInfo ResolveVietnamTimeZone()
{
    try
    {
        return TimeZoneInfo.FindSystemTimeZoneById("Asia/Saigon");
    }
    catch (TimeZoneNotFoundException)
    {
        return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
    }
}

public partial class Program;
