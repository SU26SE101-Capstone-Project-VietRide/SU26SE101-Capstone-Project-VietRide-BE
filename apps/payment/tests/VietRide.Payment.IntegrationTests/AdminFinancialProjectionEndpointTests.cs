using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using VietRide.Payment.Application.Features.Management;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.IntegrationTests;

public sealed class AdminFinancialProjectionEndpointTests
    : IClassFixture<FinancialManagementWebApplicationFactory>
{
    private static readonly Guid AdminId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private readonly FinancialManagementWebApplicationFactory _factory;

    public AdminFinancialProjectionEndpointTests(FinancialManagementWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SettlementList_ReturnsAdditiveOperatorAndSettledByInsideAdrEnvelope()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        _factory.Financial.ListAdminSettlementsAsync(
                default!, default, default, default, default, default, default)
            .ReturnsForAnyArgs(PagedResult<AdminSettlementDto>.Create(
            [
                new AdminSettlementDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    operatorId,
                    "SETTLED",
                    DateTimeOffset.UtcNow,
                    500_000,
                    "ADMIN_MANUAL",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    0,
                    null,
                    null,
                    new FinancialOperatorDto(operatorId, "Operator A", null, "+84901234567"),
                    new FinancialActorDto(AdminId, "System Admin", "admin@vietride.vn", "SYSTEM_ADMIN")),
            ], 1, 20, 1));
        using var client = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var response = await client.GetAsync("/v1/admin/trip-settlements?page=1&pageSize=20");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.GetProperty("data").GetProperty("items")[0];
        item.GetProperty("operator").GetProperty("name").GetString().Should().Be("Operator A");
        item.GetProperty("settledBy").GetProperty("userId").GetGuid().Should().Be(AdminId);
        item.GetProperty("settledBy").GetProperty("role").GetString().Should().Be("SYSTEM_ADMIN");
    }

    [Fact]
    public async Task OperatorLedger_ReturnsAdditiveNoteAndActorInsideAdrEnvelope()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        _factory.Financial.ListOperatorLedgerAsync(
                default, default!, default, default, default, default)
            .ReturnsForAnyArgs(PagedResult<LedgerEntryDto>.Create(
            [
                new LedgerEntryDto(
                    Guid.NewGuid(),
                    null,
                    "ADJUSTMENT",
                    10_000,
                    "MANUAL",
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    "manual correction",
                    "USER",
                    new FinancialActorDto(actorId, "System Admin", "admin@vietride.vn", "SYSTEM_ADMIN")),
            ], 1, 20, 1));
        using var client = _factory.CreateOperatorRoleClient("OPERATOR_ADMIN", operatorId);

        var response = await client.GetAsync("/v1/operator/ledger?page=1&pageSize=20");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.GetProperty("data").GetProperty("items")[0];
        item.GetProperty("note").GetString().Should().Be("manual correction");
        item.GetProperty("actorType").GetString().Should().Be("USER");
        item.GetProperty("actor").GetProperty("email").GetString().Should().Be("admin@vietride.vn");
    }

    [Fact]
    public async Task OperatorSettlements_ReturnAdditiveSettledByInsideAdrEnvelope()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        _factory.Financial.ListOperatorSettlementsAsync(
                default, default!, default, default, default)
            .ReturnsForAnyArgs(PagedResult<SettlementDto>.Create(
            [
                new SettlementDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "SETTLED",
                    DateTimeOffset.UtcNow,
                    500_000,
                    "ADMIN_MANUAL",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    new FinancialActorDto(actorId, "System Admin", "admin@vietride.vn", "SYSTEM_ADMIN")),
            ], 1, 20, 1));
        using var client = _factory.CreateOperatorRoleClient("OPERATOR_ADMIN", operatorId);

        var response = await client.GetAsync("/v1/operator/trip-settlements?page=1&pageSize=20");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.GetProperty("data").GetProperty("items")[0];
        item.GetProperty("settledBy").GetProperty("userId").GetGuid().Should().Be(actorId);
        item.GetProperty("settledBy").GetProperty("role").GetString().Should().Be("SYSTEM_ADMIN");
    }

    [Fact]
    public async Task PlatformTransactions_ReturnUserOrSystemActorWithoutChangingLegacyFields()
    {
        _factory.Reset();
        _factory.Financial.ListPlatformTransactionsAsync(default!, default, default, default)
            .ReturnsForAnyArgs(PagedResult<PlatformWalletTransactionDto>.Create(
            [
                new PlatformWalletTransactionDto(
                    Guid.NewGuid(), "CREDIT", 100_000, 0, 100_000, "SUBSCRIPTION_PAYMENT",
                    Guid.NewGuid(), "automated", DateTimeOffset.UtcNow, "SYSTEM", null,
                    BusinessGroup: "SUBSCRIPTION", CashFlowPurpose: "PLATFORM_REVENUE",
                    Allocations: []),
                new PlatformWalletTransactionDto(
                    Guid.NewGuid(), "DEBIT", 10_000, 100_000, 90_000, "MANUAL_ADJUSTMENT",
                    null, "manual", DateTimeOffset.UtcNow, "USER",
                    new FinancialActorDto(AdminId, "System Admin", "admin@vietride.vn", "SYSTEM_ADMIN")),
            ], 1, 20, 2));
        using var client = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var response = await client.GetAsync("/v1/admin/platform-wallet/transactions");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        var items = document.RootElement.GetProperty("data").GetProperty("items");
        items[0].GetProperty("actorType").GetString().Should().Be("SYSTEM");
        items[0].GetProperty("businessGroup").GetString().Should().Be("SUBSCRIPTION");
        items[0].GetProperty("cashFlowPurpose").GetString().Should().Be("PLATFORM_REVENUE");
        items[0].GetProperty("allocations").GetArrayLength().Should().Be(0);
        items[0].GetProperty("actor").ValueKind.Should().Be(JsonValueKind.Null);
        items[1].GetProperty("actorType").GetString().Should().Be("USER");
        items[1].GetProperty("actor").GetProperty("email").GetString()
            .Should().Be("admin@vietride.vn");
        items[1].GetProperty("amount").GetInt64().Should().Be(10_000);
    }

    [Fact]
    public async Task FinancialSearch_IsForwardedBeforeServicePaging()
    {
        _factory.Reset();
        _factory.Financial.ListAdminSettlementsAsync(
                default!, default, default, default, default, default, default, default)
            .ReturnsForAnyArgs(PagedResult<AdminSettlementDto>.Create([], 1, 20, 0));
        _factory.Financial.ListPlatformTransactionsAsync(
                default!, default, default, default, default)
            .ReturnsForAnyArgs(PagedResult<PlatformWalletTransactionDto>.Create([], 1, 20, 0));
        using var client = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var settlements = await client.GetAsync("/v1/admin/trip-settlements?search=trip-ref");
        var transactions = await client.GetAsync("/v1/admin/platform-wallet/transactions?search=actor-name");

        settlements.StatusCode.Should().Be(HttpStatusCode.OK);
        transactions.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Financial.Received(1).ListAdminSettlementsAsync(
            Arg.Any<PageOptions>(),
            null,
            null,
            null,
            false,
            null,
            Arg.Any<CancellationToken>(),
            "trip-ref");
        await _factory.Financial.Received(1).ListPlatformTransactionsAsync(
            Arg.Any<PageOptions>(),
            null,
            null,
            Arg.Any<CancellationToken>(),
            "actor-name");
    }

    [Fact]
    public async Task AdminFinancialProjection_ForwardsAllocationFiltersBeforePaging()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        _factory.Financial.ListPlatformTransactionsAsync(
                default!, default, default, default, default, default, default, default, default)
            .ReturnsForAnyArgs(PagedResult<PlatformWalletTransactionDto>.Create([], 1, 20, 0));
        using var client = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(
            $"/v1/admin/platform-wallet/transactions?operatorId={operatorId:D}&tripId={tripId:D}&businessGroup=SETTLEMENT&cashFlowPurpose=OPERATOR_PAYOUT");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Financial.Received(1).ListPlatformTransactionsAsync(
            Arg.Is<PageOptions>(item => item.Page == 1 && item.PageSize == 20),
            null,
            null,
            Arg.Any<CancellationToken>(),
            null,
            operatorId,
            tripId,
            "SETTLEMENT",
            "OPERATOR_PAYOUT");
    }

    [Fact]
    public async Task AdminFinancialProjection_ReconciliationSummaryUsesIctRange()
    {
        _factory.Reset();
        _factory.Financial.GetPlatformWalletReconciliationSummaryAsync(
                default, default, default)
            .ReturnsForAnyArgs(new PlatformWalletReconciliationSummaryDto(
                new PlatformWalletReconciliationSnapshotDto(1_000, 600, 100, 200, 300, 1, 0, 0),
                new PlatformWalletReconciliationPeriodDto(
                    new DateOnly(2026, 9, 1),
                    new DateOnly(2026, 9, 30),
                    "Asia/Ho_Chi_Minh",
                    50,
                    400),
                DateTimeOffset.UtcNow));
        using var client = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(
            "/v1/admin/platform-wallet/reconciliation-summary?from=2026-09-01&to=2026-09-30");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("data").GetProperty("snapshot")
            .GetProperty("outstandingOperatorPayableVnd").GetInt64().Should().Be(600);
        await _factory.Financial.Received(1).GetPlatformWalletReconciliationSummaryAsync(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WalletReconciliation_OperatorSummaryAndLedgerRemainTenantScoped()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        _factory.Financial.GetOperatorWalletAsync(operatorId, default)
            .ReturnsForAnyArgs(new OperatorWalletDto(
                operatorId,
                400,
                200,
                300,
                DateTimeOffset.UtcNow,
                Reconciliation: new OperatorWalletReconciliationDto(600, 100, 200, 300)));
        using var client = _factory.CreateOperatorRoleClient("OPERATOR_ADMIN", operatorId);

        var response = await client.GetAsync("/v1/operator/wallet");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("data").GetProperty("reconciliation")
            .GetProperty("eligibleForSettlementVnd").GetInt64().Should().Be(300);
        await _factory.Financial.Received(1).GetOperatorWalletAsync(
            operatorId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WalletReconciliation_OperatorMissingTenantReturnsForbidden()
    {
        _factory.Reset();
        using var client = _factory.CreateRoleClient("OPERATOR_ADMIN");

        var response = await client.GetAsync("/v1/operator/wallet");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminFinancialProjection_ExportUsesSameAllocationFilters()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        _factory.Financial.ExportPlatformTransactionsAsync(
                default!, default, default, default, default, default, default, default, default)
            .ReturnsForAnyArgs(new VietRide.Shared.Application.Reporting.ExcelReportStream(
                new MemoryStream([1, 2, 3]),
                "platform-wallet.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        using var client = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(
            $"/v1/admin/platform-wallet/transactions/export?operatorId={operatorId:D}&businessGroup=SETTLEMENT");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Financial.Received(1).ExportPlatformTransactionsAsync(
            Arg.Any<PageOptions>(),
            null,
            null,
            null,
            operatorId,
            null,
            "SETTLEMENT",
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WalletReconciliation_OperatorExportUsesJwtTenant()
    {
        _factory.Reset();
        var operatorId = Guid.NewGuid();
        _factory.Financial.ExportOperatorWalletReconciliationAsync(
                default, default, default, default)
            .ReturnsForAnyArgs(new VietRide.Shared.Application.Reporting.ExcelReportStream(
                new MemoryStream([1, 2, 3]),
                "operator-wallet.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        using var client = _factory.CreateOperatorRoleClient("OPERATOR_ADMIN", operatorId);

        var response = await client.GetAsync(
            "/v1/operator/wallet/reconciliation/export?from=2026-09-01&to=2026-09-30");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Financial.Received(1).ExportOperatorWalletReconciliationAsync(
            operatorId,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManualPlatformAdjustment_UsesAuthenticatedSubAndRequiresIdempotencyKey()
    {
        _factory.Reset();
        _factory.Financial.AdjustPlatformWalletAsync(
                Arg.Any<AdjustmentRequest>(), AdminId, Arg.Any<CancellationToken>())
            .Returns(new AdjustmentResult(
                Guid.NewGuid(), "CREDIT", 10_000, 0, 10_000, "MANUAL_ADJUSTMENT", null,
                "correction", DateTimeOffset.UtcNow));
        using var client = _factory.CreateRoleClient("SYSTEM_ADMIN");
        using var missingKey = await client.PostAsJsonAsync(
            "/v1/admin/platform-wallet/adjust",
            new { type = "CREDIT", amount = 10_000, note = "correction", actorUserId = Guid.NewGuid() });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/platform-wallet/adjust")
        {
            Content = JsonContent.Create(new
            {
                type = "CREDIT",
                amount = 10_000,
                note = "correction",
                actorUserId = Guid.NewGuid(),
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        missingKey.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Financial.Received(1).AdjustPlatformWalletAsync(
            Arg.Is<AdjustmentRequest>(item => item.Note == "correction"),
            AdminId,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("/v1/admin/trip-settlements")]
    [InlineData("/v1/admin/platform-wallet/transactions")]
    [InlineData("/v1/admin/platform-wallet/reconciliation-summary")]
    [InlineData("/v1/admin/platform-wallet/transactions/export")]
    public async Task FinancialProjectionReads_RejectOperatorAdmin(string path)
    {
        _factory.Reset();
        using var client = _factory.CreateRoleClient("OPERATOR_ADMIN");

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

public sealed class FinancialManagementWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly Guid AuthenticatedAdminId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public IFinancialManagementService Financial { get; } = Substitute.For<IFinancialManagementService>();

    public void Reset()
    {
        Financial.ClearReceivedCalls();
    }

    public HttpClient CreateRoleClient(string role)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Role", role);
        return client;
    }

    public HttpClient CreateOperatorRoleClient(string role, Guid operatorId)
    {
        var client = CreateRoleClient(role);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Operator-Id", operatorId.ToString());
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
        builder.UseSetting(
            "INTERNAL_JWT_SECRET",
            "ui05-financial-test-secret-at-least-32-characters");
        builder.UseSetting("InvoiceStorage:Provider", "E2E_LOCAL");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFinancialManagementService>();
            services.RemoveAll<IConnectionMultiplexer>();
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton(Financial);
            services.AddSingleton<IConnectionMultiplexer>(InMemoryIdempotencyRedis.Create());
            services.AddSingleton<IUnitOfWork, PassthroughUnitOfWork>();
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    private sealed class PassthroughUnitOfWork : IUnitOfWork
    {
        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
            => operation();

        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "FinancialProjectionTest";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (string.IsNullOrWhiteSpace(role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim>
            {
                new("sub", AuthenticatedAdminId.ToString()),
                new(ClaimTypes.Role, role),
            };
            if (Guid.TryParse(Request.Headers["X-Test-Operator-Id"].ToString(), out var operatorId))
                claims.Add(new Claim("operatorId", operatorId.ToString()));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
