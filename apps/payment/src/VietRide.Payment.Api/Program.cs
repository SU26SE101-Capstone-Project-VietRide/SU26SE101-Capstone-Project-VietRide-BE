using Hangfire;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;
using VietRide.Payment.Application;
using VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;
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
    handlerAssemblies:
    [
        typeof(ApplicationAssemblyMarker).Assembly,
        typeof(BatchChargePaymentCommandHandler).Assembly,
    ]);
builder.Services.AddScoped<IBatchChargePaymentDbContext>(sp => sp.GetRequiredService<PaymentDbContext>());

var registerMessaging = !builder.Environment.IsEnvironment("Testing");
if (registerMessaging)
{
    builder.Services.AddVietRideMessaging(builder.Configuration);
    builder.Services.AddPaymentHangfire(builder.Configuration);
    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = builder.Configuration.GetValue("Hangfire:WorkerCount", 2);
    });
}

var redisUrl = builder.Configuration["REDIS_URL"]
    ?? Environment.GetEnvironmentVariable("REDIS_URL")
    ?? "localhost:6379";
var redisOptions = ConfigurationOptions.Parse(redisUrl);
redisOptions.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));

builder.Services.AddInfrastructure(builder.Configuration, registerConsumers: registerMessaging);
builder.Services.AddVietRideIdempotency("payment");

var app = builder.Build();

if (!IsWebApplicationFactoryHost())
{
    await using var scope = app.Services.CreateAsyncScope();
    var paymentDb = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    // Migrate, then reload the Npgsql type catalog so the native enums (payment_status,
    // wallet_transaction_type, …) resolve on a fresh DB — otherwise the first enum read
    // fails at runtime with DataTypeName '-'.
    await paymentDb.MigrateAndReloadTypesAsync();

    // Ensure the singleton PlatformWallet row exists (the ledger every booking charge/refund
    // moves money through). It lives only in db-schema/.../seed.sql, which the container does
    // not run — without it GetSingletonAsync() throws on 0 rows. Idempotent.
    await paymentDb.Database.ExecuteSqlRawAsync(
        "INSERT INTO vietride_payment.platform_wallets (balance, currency) "
        + "SELECT 0, 'VND' WHERE NOT EXISTS (SELECT 1 FROM vietride_payment.platform_wallets);");
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
    recurringJobs.AddOrUpdate<RefundFailureRetryJob>(
        RefundFailureRetryJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        "*/10 * * * *");
    recurringJobs.AddOrUpdate<PaymentExpiredJob>(
        PaymentExpiredJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        Cron.Minutely());
    recurringJobs.AddOrUpdate<Day38PaymentContextBackfillJob>(
        Day38PaymentContextBackfillJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        "*/5 * * * *");
    recurringJobs.AddOrUpdate<Day38RevenueLedgerBackfillJob>(
        Day38RevenueLedgerBackfillJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        "*/10 * * * *");
    recurringJobs.AddOrUpdate<TripSettlementEligibilityFlagJob>(
        TripSettlementEligibilityFlagJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        "0 19 * * *");
    recurringJobs.AddOrUpdate<TripSettlementWeeklyAutoSettleJob>(
        TripSettlementWeeklyAutoSettleJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        "0 2 * * 1");
    recurringJobs.AddOrUpdate<TripSettlementStuckAlertJob>(
        TripSettlementStuckAlertJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        Cron.Hourly());
}

app.Run();

static bool IsWebApplicationFactoryHost()
    => AppDomain.CurrentDomain.GetAssemblies()
        .Any(assembly => assembly.GetName().Name == "Microsoft.AspNetCore.Mvc.Testing");

// Expose Program for WebApplicationFactory<Program> in integration tests.
public partial class Program;
