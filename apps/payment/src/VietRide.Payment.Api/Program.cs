using Serilog;
using StackExchange.Redis;
using VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;
using VietRide.Payment.Infrastructure;
using VietRide.Shared.Application.DependencyInjection;
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
    handlerAssemblies: [typeof(BatchChargePaymentCommandHandler).Assembly]);
builder.Services.AddScoped<IBatchChargePaymentDbContext>(sp => sp.GetRequiredService<PaymentDbContext>());

var redisUrl = builder.Configuration["REDIS_URL"]
    ?? Environment.GetEnvironmentVariable("REDIS_URL")
    ?? "localhost:6379";
var redisOptions = ConfigurationOptions.Parse(redisUrl);
redisOptions.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
builder.Services.AddVietRideIdempotency("payment");

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
