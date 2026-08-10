using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Refunds;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.Internal.Revenue.BackfillParcelVoucherReversals;
using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;
using VietRide.Payment.Application.Features.Invoices;
using VietRide.Payment.Application.Features.Management;
using VietRide.Payment.Application.Features.OperatorWallets.BootstrapOperatorWallet;
using VietRide.Payment.Application.Features.Payments.MarkPaymentRefunded;
using VietRide.Payment.Application.Features.Settlements.HandleTripTerminal;
using VietRide.Payment.Application.Features.Settlements.SettleTrip;
using VietRide.Payment.Application.Features.Wallets.BootstrapWallet;
using VietRide.Payment.Application.Services;
using VietRide.Payment.Infrastructure.Caching;
using VietRide.Payment.Infrastructure.Http;
using VietRide.Payment.Infrastructure.Invoices;
using VietRide.Payment.Infrastructure.Jobs;
using VietRide.Payment.Infrastructure.Maintenance;
using VietRide.Payment.Infrastructure.Management;
using VietRide.Payment.Infrastructure.Messaging;
using VietRide.Payment.Infrastructure.Persistence.Repositories;
using VietRide.Payment.Infrastructure.Refunds;
using VietRide.Payment.Infrastructure.VnPay;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Reporting;

namespace VietRide.Payment.Infrastructure.DependencyInjection;

/// <summary>
/// Registers Payment Infrastructure services such as Redis (required by idempotency)
/// and outbound HTTP helpers.
/// </summary>
/// <remarks>
/// DB-CONTEXT GUARD: this method MUST NOT call AddVietRideDbContext / AddDbContext.
/// The PaymentDbContext is already registered at Program.cs via AddVietRideDbContext.
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    public const string HangfireSchemaName = "hangfire";

    /// <summary>
    /// Adds Hangfire storage for Payment background jobs.
    /// </summary>
    public static IServiceCollection AddPaymentHangfire(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IExcelReportWriter, ClosedXmlExcelReportWriter>();
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                connectionString,
                new PostgreSqlStorageOptions
                {
                    SchemaName = HangfireSchemaName,
                    PrepareSchemaIfNecessary = true,
                }));

        return services;
    }

    /// <summary>
    /// Adds Payment Infrastructure services to the DI container.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool registerConsumers = true)
    {
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ITopUpRequestRepository, TopUpRequestRepository>();
        services.AddScoped<IPlatformWalletRepository, PlatformWalletRepository>();
        services.AddScoped<IRefundFailureLogRepository, RefundFailureLogRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IInvoiceNumberCounterRepository, InvoiceNumberCounterRepository>();
        services.AddScoped<IInvoiceLifecycleService, InvoiceLifecycleService>();
        services.AddScoped<IInvoiceJobScheduler, InvoiceJobScheduler>();
        services.AddScoped<IFinancialManagementService, FinancialManagementService>();
        services.AddScoped<IInvoicePdfGenerator, PdfSharpInvoicePdfGenerator>();
        var invoiceStorageProvider = configuration[$"{InvoiceStorageOptions.SectionName}:Provider"];
        if (string.Equals(invoiceStorageProvider, "E2E_LOCAL", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IInvoiceStorage, E2eLocalInvoiceStorage>();
        }
        else
        {
            services.AddScoped<IInvoiceStorage, GoogleCloudInvoiceStorage>();
            services.AddSingleton(_ => GoogleCredential.GetApplicationDefault());
            services.AddSingleton(sp => StorageClient.Create(sp.GetRequiredService<GoogleCredential>()));
        }
        services.AddScoped<IOperatorWalletRepository, OperatorWalletRepository>();
        services.AddScoped<IOperatorWalletTransactionRepository, OperatorWalletTransactionRepository>();
        services.AddScoped<IOperatorLedgerEntryRepository, OperatorLedgerEntryRepository>();
        services.AddScoped<IParcelVoucherReversalBackfillService, ParcelVoucherReversalBackfillService>();
        services.AddScoped<IOperatorTripSettlementRepository, OperatorTripSettlementRepository>();
        services.AddScoped<IRevenueAnalyticsRepository, RevenueAnalyticsRepository>();
        services.AddScoped<IRevenueCacheStore, RedisRevenueCacheStore>();
        services.AddScoped<IRevenueReportCache, RedisRevenueReportCache>();
        services.AddScoped<IFinancialActorPrivacyStore, FinancialActorPrivacyStore>();
        services.AddScoped<IProcessedIntegrationEventRepository, ProcessedIntegrationEventRepository>();
        services.AddScoped<IIntegrationEventInbox, PaymentIntegrationEventInbox>();
        services.AddScoped<IRevenueLedgerWriter, RevenueLedgerWriter>();
        services.AddScoped<TripTerminalSettlementService>();
        services.AddScoped<TripSettlementService>();
        services.AddScoped<IRefundRetryExecutor, WalletRefundRetryExecutor>();
        services.AddScoped<RefundRetryService>();
        services.Configure<VnPayOptions>(options =>
        {
            configuration.GetSection(VnPayOptions.SectionName).Bind(options);
            options.TmnCode = configuration["VNPAY_TMN_CODE"] ?? options.TmnCode;
            options.HashSecret = configuration["VNPAY_HASH_SECRET"] ?? options.HashSecret;
            options.BaseUrl = configuration["VNPAY_BASE_URL"] ?? options.BaseUrl;
            options.WebReturnUrl = configuration["VNPAY_WEB_RETURN_URL"] ?? options.WebReturnUrl;
            options.MobileSdkReturnUrl = configuration["VNPAY_MOBILE_SDK_RETURN_URL"] ?? options.MobileSdkReturnUrl;
            options.SdkScheme = configuration["VNPAY_SDK_SCHEME"] ?? options.SdkScheme;
            options.IpnUrl = configuration["VNPAY_IPN_URL"] ?? options.IpnUrl;
            if (bool.TryParse(configuration["VNPAY_IS_SANDBOX"], out var isSandbox))
            {
                options.IsSandbox = isSandbox;
            }

            if (bool.TryParse(configuration["VNPAY_WEB_ENABLED"], out var webEnabled))
            {
                options.WebEnabled = webEnabled;
            }

            if (bool.TryParse(configuration["VNPAY_MOBILE_SDK_ENABLED"], out var mobileSdkEnabled))
            {
                options.MobileSdkEnabled = mobileSdkEnabled;
            }

            if (int.TryParse(configuration["VNPAY_PAYMENT_TIMEOUT_MINUTES"], out var timeoutMinutes))
            {
                options.PaymentTimeoutMinutes = timeoutMinutes;
            }

            if (long.TryParse(configuration["WALLET_TOP_UP_MIN_VND"], out var minimumTopUpAmount))
            {
                options.MinimumTopUpAmount = minimumTopUpAmount;
            }
        });
        services.AddOptions<VnPayOptions>()
            .Validate(options => options.PaymentTimeoutMinutes > 0, "VNPay payment timeout must be positive.")
            .Validate(options => IsAbsoluteHttpsUrl(options.BaseUrl)
                && IsAllowedReturnUrl(options.WebReturnUrl)
                && IsAbsoluteHttpsUrl(options.MobileSdkReturnUrl)
                && IsAbsoluteHttpsUrl(options.IpnUrl), "VNPay URLs must be absolute HTTPS URLs.")
            .Validate(options => HasCanonicalVnPayIpnPath(options.IpnUrl), "VNPay IPN URL must use /v1/payments/vnpay-ipn.")
            .Validate(options => HasCanonicalVnPayMobileSdkReturnPath(options.MobileSdkReturnUrl),
                "VNPay Mobile SDK return URL must use /v1/payments/vnpay-mobile-sdk-return.")
            .Validate(options => Uri.CheckSchemeName(options.SdkScheme), "VNPay SDK scheme must be a valid URI scheme.")
            .Validate(options => !string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Production", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(options.TmnCode) && !string.IsNullOrWhiteSpace(options.HashSecret)),
                "VNPay TMN code and hash secret are required in production.")
            .ValidateOnStart();
        services.AddScoped<IVnPayClient, VnPayClient>();
        services.AddSingleton<IVnPayRedirectUrlValidator, VnPayRedirectUrlValidator>();
        services.Configure<InvoicePdfOptions>(configuration.GetSection(InvoicePdfOptions.SectionName));
        services.Configure<InvoiceStorageOptions>(configuration.GetSection(InvoiceStorageOptions.SectionName));
        services.Configure<OperatorWebOptions>(configuration.GetSection(OperatorWebOptions.SectionName));
        services.Configure<InvoiceBackfillOptions>(configuration.GetSection(InvoiceBackfillOptions.SectionName));

        if (registerConsumers)
        {
            services.AddVietRideEventConsumer<UserCreatedIntegrationEvent, BootstrapWalletCommandHandler>(options =>
            {
                options.QueueName = "payment.wallet-bootstrap";
                options.BindingKeys = [UserCreatedIntegrationEvent.EventType];
            });
            services.AddVietRideEventConsumer<IdentityUserDeletedIntegrationEvent, IdentityUserDeletedIntegrationEventHandler>(options =>
            {
                options.QueueName = "payment.identity-user-deleted";
                options.BindingKeys = [IdentityUserDeletedIntegrationEvent.EventType];
            });

            // BSOT §8.4: Payment consumes its own canonical wallet-credit event to drive the
            // originating Payment row to REFUNDED for refund credits (BOOKING_REFUND / PARCEL_REFUND).
            services.AddVietRideEventConsumer<BookingCancelledIntegrationEvent, BookingCancelledIntegrationEventHandler>(options =>
            {
                options.QueueName = "payment.booking-refund";
                options.BindingKeys = [BookingCancelledIntegrationEvent.EventType];
            });

            services.AddVietRideEventConsumer<BookingPaymentRefundRequestedIntegrationEvent, BookingPaymentRefundRequestedIntegrationEventHandler>(options =>
            {
                options.QueueName = "payment.booking-payment-refund-requested";
                options.BindingKeys = [BookingPaymentRefundRequestedIntegrationEvent.EventType];
            });

            services.AddVietRideEventConsumer<WalletCreditedConsumerEvent, MarkPaymentRefundedCommandHandler>(options =>
            {
                options.QueueName = "payment.payment-refunded";
                options.BindingKeys = [WalletCreditedConsumerEvent.EventType];
            });

            services.AddVietRideEventConsumer<ParcelRefundInitiatedIntegrationEvent, ParcelRefundInitiatedIntegrationEventHandler>(options =>
            {
                options.QueueName = "payment.parcel-refund-initiated";
                options.BindingKeys = [ParcelRefundInitiatedIntegrationEvent.EventTypeValue];
            });

            services.AddVietRideEventConsumer<OperatorApprovedConsumerEvent, BootstrapOperatorWalletEventHandler>(options =>
            {
                options.QueueName = "payment.operator-wallet-bootstrap";
                options.BindingKeys = [OperatorApprovedConsumerEvent.EventTypeValue];
            });
            services.AddVietRideEventConsumer<TripCompletedConsumerEvent, TripCompletedSettlementEventHandler>(options =>
            {
                options.QueueName = "payment.trip-completed-settlement";
                options.BindingKeys = [TripCompletedConsumerEvent.EventTypeValue];
            });
            services.AddVietRideEventConsumer<TripDisruptedConsumerEvent, TripDisruptedSettlementEventHandler>(options =>
            {
                options.QueueName = "payment.trip-disrupted-settlement";
                options.BindingKeys = [TripDisruptedConsumerEvent.EventTypeValue];
            });
            services.AddVietRideEventConsumer<SubscriptionPaymentSucceededInvoiceEvent, SubscriptionPaymentSucceededInvoiceHandler>(options =>
            {
                options.QueueName = "payment.subscription-invoice";
                options.BindingKeys = [SubscriptionPaymentSucceededInvoiceEvent.EventTypeValue];
            });
            services.AddHostedService<InvoiceJobsRegistrationHostedService>();
        }

        // Redis — required by IdempotencyMiddleware (wired in Program.cs via AddVietRideIdempotency).
        // Falls back gracefully if REDIS_URL is absent (AbortOnConnectFail = false).
        var redisUrl = configuration["REDIS_URL"]
            ?? Environment.GetEnvironmentVariable("REDIS_URL")
            ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisUrl);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisOptions));

        // Internal JWT provider — used by outbound delegating handlers.
        services.AddSingleton<IInternalJwtTokenProvider, InternalJwtTokenFactory>();
        services.AddHttpContextAccessor();
        services.AddTransient<InternalJwtDelegatingHandler>();
        services.AddTransient<CorrelationIdDelegatingHandler>();

        services.Configure<Day38PaymentContextBackfillOptions>(
            configuration.GetSection(Day38PaymentContextBackfillOptions.SectionName));
        services.Configure<Day38RevenueLedgerBackfillOptions>(
            configuration.GetSection(Day38RevenueLedgerBackfillOptions.SectionName));

        RegisterPaymentContextOwnerClient(
            services,
            Day38PaymentContextBackfillJob.BookingHttpClientName,
            configuration["Booking:BaseUrl"]
                ?? configuration["BOOKING_SERVICE_BASE_URL"]
                ?? "http://booking:8080");
        RegisterPaymentContextOwnerClient(
            services,
            Day38PaymentContextBackfillJob.ParcelHttpClientName,
            configuration["Parcel:BaseUrl"]
                ?? configuration["PARCEL_SERVICE_BASE_URL"]
                ?? "http://parcel:8080");

        RegisterPlatformReportClient<IBookingPlatformReportClient, BookingPlatformReportClient>(
            services,
            configuration["Booking:BaseUrl"]
                ?? configuration["BOOKING_SERVICE_BASE_URL"]
                ?? "http://booking:8080");
        RegisterPlatformReportClient<ITripPlatformReportClient, TripPlatformReportClient>(
            services,
            configuration["Trip:BaseUrl"]
                ?? configuration["TRIP_SERVICE_BASE_URL"]
                ?? "http://trip:8080");
        RegisterPlatformReportClient<ITripRevenueAnalyticsClient, TripRevenueAnalyticsClient>(
            services,
            configuration["Trip:BaseUrl"]
                ?? configuration["TRIP_SERVICE_BASE_URL"]
                ?? "http://trip:8080");
        RegisterPlatformReportClient<IParcelPlatformReportClient, ParcelPlatformReportClient>(
            services,
            configuration["Parcel:BaseUrl"]
                ?? configuration["PARCEL_SERVICE_BASE_URL"]
                ?? "http://parcel:8080");
        RegisterPlatformReportClient<IIdentityOperatorSummaryClient, IdentityOperatorSummaryClient>(
            services,
            configuration["Identity:BaseUrl"]
                ?? configuration["IDENTITY_SERVICE_BASE_URL"]
                ?? "http://identity:5001");
        RegisterPlatformReportClient<IIdentityFinancialProjectionClient, IdentityFinancialProjectionClient>(
            services,
            configuration["Identity:BaseUrl"]
                ?? configuration["IDENTITY_SERVICE_BASE_URL"]
                ?? "http://identity:5001");

        return services;
    }

    private static void RegisterPlatformReportClient<TClient, TImplementation>(
        IServiceCollection services,
        string baseUrl)
        where TClient : class
        where TImplementation : class, TClient
    {
        services.AddHttpClient<TClient, TImplementation>(client =>
        {
            client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(5);
        });
    }

    private static void RegisterPaymentContextOwnerClient(
        IServiceCollection services,
        string name,
        string baseUrl)
    {
        services.AddHttpClient(name, client =>
            {
                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddHttpMessageHandler<InternalJwtDelegatingHandler>();
    }

    private static bool IsAbsoluteHttpsUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsAllowedReturnUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps
                || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    private static bool HasCanonicalVnPayIpnPath(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && string.Equals(uri.AbsolutePath.TrimEnd('/'), "/v1/payments/vnpay-ipn", StringComparison.OrdinalIgnoreCase);

    private static bool HasCanonicalVnPayMobileSdkReturnPath(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && string.Equals(
                uri.AbsolutePath.TrimEnd('/'),
                "/v1/payments/vnpay-mobile-sdk-return",
                StringComparison.OrdinalIgnoreCase);
}
