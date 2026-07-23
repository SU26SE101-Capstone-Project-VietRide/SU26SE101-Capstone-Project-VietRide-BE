using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using VietRide.Identity.Api.Controllers;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Admin.CreateOperator;
using VietRide.Identity.Application.Features.Operators.RegisterOperator;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Identity.Infrastructure.DependencyInjection;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class OperatorsControllerTests :
    IClassFixture<AuthWebApplicationFactory>,
    IClassFixture<OperatorsControllerTests.DbBackedOperatorsFactory>
{
    private static readonly Guid SystemAdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly AuthWebApplicationFactory _factory;
    private readonly DbBackedOperatorsFactory _dbFactory;

    public OperatorsControllerTests(AuthWebApplicationFactory factory, DbBackedOperatorsFactory dbFactory)
    {
        _factory = factory;
        _dbFactory = dbFactory;
    }

    [Fact]
    public void RegisterEndpoint_HasSwaggerResponseAnnotations()
    {
        var method = typeof(OperatorsController).GetMethod(nameof(OperatorsController.Register));

        var responseTypes = method!
            .GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: false)
            .Cast<ProducesResponseTypeAttribute>()
            .Select(x => (x.StatusCode, x.Type))
            .ToList();

        responseTypes.Should().Contain((StatusCodes.Status201Created, typeof(ApiResponse<RegisterOperatorResponseDto>)));
        responseTypes.Should().Contain(x => x.StatusCode == StatusCodes.Status409Conflict);
        responseTypes.Should().Contain(x => x.StatusCode == StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void AdminCreateEndpoint_HasSwaggerResponseAnnotations()
    {
        var method = typeof(AdminOperatorsController).GetMethod(nameof(AdminOperatorsController.Create));

        var responseTypes = method!
            .GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: false)
            .Cast<ProducesResponseTypeAttribute>()
            .Select(x => (x.StatusCode, x.Type))
            .ToList();

        responseTypes.Should().Contain((StatusCodes.Status201Created, typeof(ApiResponse<CreateOperatorResponseDto>)));
        responseTypes.Should().Contain(x => x.StatusCode == StatusCodes.Status401Unauthorized);
        responseTypes.Should().Contain(x => x.StatusCode == StatusCodes.Status403Forbidden);
        responseTypes.Should().Contain(x => x.StatusCode == StatusCodes.Status409Conflict);
        responseTypes.Should().Contain(x => x.StatusCode == StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void AddInfrastructure_ResolvesOperatorRepositories()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddScoped<IdentityDbContext>(_ => null!);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REDIS_URL"] = "localhost:6379",
            })
            .Build();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IOperatorRepository>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IOperatorSubscriptionRepository>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ISubscriptionPlanRepository>().Should().NotBeNull();
    }

    [Fact]
    public async Task Register_Returns201Created()
    {
        var sender = new RecordingSender();
        var controller = new OperatorsController(sender);

        var result = await controller.Register(
            new RegisterOperatorRequest(
                "Operator Co",
                "operator@example.com",
                "+84901234567",
                "BRN-001",
                "TAX-001",
                "1 Street",
                "Ward",
                "District",
                "Province",
                "Operator Admin",
                "+84901234568",
                "Password123!"),
            CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        objectResult.Value.Should().BeOfType<RegisterOperatorResponseDto>();
    }

    [Fact]
    public async Task AdminCreate_Returns201CreatedAndMapsContractAddressFields()
    {
        var sender = new RecordingSender();
        var controller = new AdminOperatorsController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, SystemAdminId.ToString()),
                        new Claim(ClaimTypes.Role, UserRole.SYSTEM_ADMIN.ToString()),
                    ], "TestAuth")),
                },
            },
        };

        var result = await controller.Create(
            new CreateOperatorRequest(
                "Operator Co",
                "operator@example.com",
                "+84901234567",
                "BRN-001",
                "TAX-001",
                "1 Street",
                "Ward",
                "District",
                "Province",
                "Operator Admin",
                "+84901234568"),
            CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        objectResult.Value.Should().BeOfType<CreateOperatorResponseDto>()
            .Which.AdminUser.DisplayName.Should().Be("Operator Admin");

        var command = sender.LastRequest.Should().BeOfType<CreateOperatorCommand>().Subject;
        command.AddressStreet.Should().Be("1 Street");
        command.AddressWard.Should().Be("Ward");
        command.AddressDistrict.Should().Be("District");
        command.AddressProvince.Should().Be("Province");
    }

    [Fact]
    public async Task AdminCreate_Anonymous_Returns401UnauthorizedEnvelope()
    {
        using var client = _factory.CreateIdempotentClient();

        var response = await client.PostAsJsonAsync("/v1/admin/operators", ValidAdminCreatePayload("anonymous"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertErrorEnvelope(doc, 401, "AUTH_TOKEN_INVALID");
    }

    [Fact]
    public async Task AdminCreate_WithExplicitPlanId_Returns422ValidationEnvelope()
    {
        using var client = _factory.CreateIdempotentClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/operators")
        {
            Content = JsonContent.Create(new
            {
                name = "Operator Co",
                contactEmail = UniqueEmail("paid-plan"),
                contactPhone = "+84901234567",
                businessRegistrationNumber = $"BRN-{Guid.NewGuid():N}",
                taxCode = $"TAX-{Guid.NewGuid():N}",
                addressStreet = "1 Street",
                addressWard = "Ward",
                addressDistrict = "District",
                addressProvince = "Province",
                representativeName = "Operator Admin",
                representativePhone = "+84901234568",
                planId = Guid.NewGuid(),
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(SystemAdminId, UserRole.SYSTEM_ADMIN.ToString())}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertErrorEnvelope(doc, 422, "VALIDATION_ERROR");
        doc.RootElement.GetProperty("error").GetProperty("fields").EnumerateArray()
            .Should().Contain(field => field.GetProperty("field").GetString() == "planId");
    }

    [Fact]
    public async Task Register_HappyPath_UsesRealHandlerDbTransaction_AndPersistsOperatorAdminSubscriptionOtpAndActivityLog()
    {
        await _dbFactory.ResetAsync();
        var email = UniqueEmail("operator-register");
        var brn = $"BRN-{Guid.NewGuid():N}";
        var taxCode = $"TAX-{Guid.NewGuid():N}";
        using var client = _dbFactory.CreateIdempotentClient();

        var response = await client.PostAsJsonAsync("/v1/operators/register", ValidRegisterPayload("happy", email, brn, taxCode));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 201);
        var operatorId = doc.RootElement.GetProperty("data").GetProperty("operatorId").GetGuid();

        await using var scope = _dbFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var operatorEntity = await db.Operators.SingleAsync(x => x.Id == operatorId);
        operatorEntity.RegistrationStatus.Should().Be(OperatorRegistrationStatus.PENDING);
        operatorEntity.BusinessRegistrationNumber.Should().Be(brn);
        operatorEntity.TaxCode.Should().Be(taxCode);

        var adminUser = await db.Users.SingleAsync(x => x.OperatorId == operatorId && x.Role == UserRole.OPERATOR_ADMIN);
        adminUser.Email.Should().Be(email);
        adminUser.Status.Should().Be(UserStatus.PENDING_EMAIL_VERIFICATION);
        adminUser.PasswordHash.Should().NotBeNullOrWhiteSpace();

        var subscription = await db.OperatorSubscriptions.SingleAsync(x => x.OperatorId == operatorId);
        subscription.Status.Should().Be(SubscriptionStatus.PENDING_APPROVAL);
        subscription.CurrentOperatorUsers.Should().Be(1);

        var token = await db.EmailVerificationTokens.SingleAsync(x => x.UserId == adminUser.Id && x.Purpose == EmailVerificationPurpose.REGISTRATION);
        token.Code.Should().HaveLength(6);
        token.UsedAt.Should().BeNull();

        // OTP delivery is now via Outbox (identity.otp.requested) — verify the outbox row.
        var otpOutboxEvent = await db.Set<OutboxEvent>()
            .SingleAsync(x => x.EventType == "identity.otp.requested");
        using var otpPayload = JsonDocument.Parse(otpOutboxEvent.Payload);
        otpPayload.RootElement.GetProperty("email").GetString().Should().Be(email);
        otpPayload.RootElement.GetProperty("code").GetString().Should().Be(token.Code);
        otpPayload.RootElement.GetProperty("purpose").GetString().Should().Be("REGISTRATION");

        var activityLog = await db.ActivityLogs.SingleAsync(x => x.UserId == adminUser.Id && x.Action == ActivityLogAction.CREATE_OPERATOR);
        using var metadata = JsonDocument.Parse(activityLog.Metadata!);
        metadata.RootElement.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        metadata.RootElement.GetProperty("actorUserId").GetGuid().Should().Be(adminUser.Id);
        metadata.RootElement.GetProperty("source").GetString().Should().Be("SELF_REGISTER");
    }

    [Fact]
    public async Task AdminCreate_HappyPath_UsesRealHandlerDbTransaction_AndPersistsOperatorAdminSubscriptionTokenAndActivityLog()
    {
        await _dbFactory.ResetAsync();
        await _dbFactory.SeedSystemAdminAsync(SystemAdminId);
        var email = UniqueEmail("operator-admin-create");
        var brn = $"BRN-{Guid.NewGuid():N}";
        var taxCode = $"TAX-{Guid.NewGuid():N}";
        using var client = _dbFactory.CreateIdempotentClient();
        using var request = CreateAuthorizedAdminCreateRequest(ValidAdminCreatePayload("happy", email, brn, taxCode));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 201);
        var data = doc.RootElement.GetProperty("data");
        var operatorId = data.GetProperty("operator").GetProperty("operatorId").GetGuid();
        var adminUserId = data.GetProperty("adminUser").GetProperty("userId").GetGuid();

        await using var scope = _dbFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var operatorEntity = await db.Operators.SingleAsync(x => x.Id == operatorId);
        operatorEntity.RegistrationStatus.Should().Be(OperatorRegistrationStatus.APPROVED);
        operatorEntity.ApprovedByUserId.Should().Be(SystemAdminId);

        var adminUser = await db.Users.SingleAsync(x => x.Id == adminUserId);
        adminUser.OperatorId.Should().Be(operatorId);
        adminUser.Role.Should().Be(UserRole.OPERATOR_ADMIN);
        adminUser.Status.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD);
        adminUser.PasswordHash.Should().BeNull();

        var subscription = await db.OperatorSubscriptions.SingleAsync(x => x.OperatorId == operatorId);
        subscription.Status.Should().Be(SubscriptionStatus.ACTIVE);
        subscription.StartedAt.Should().NotBeNull();
        subscription.ExpiresAt.Should().Be(subscription.StartedAt!.Value.AddDays(30));
        subscription.CurrentOperatorUsers.Should().Be(1);

        var token = await db.EmailVerificationTokens.SingleAsync(x => x.UserId == adminUserId && x.Purpose == EmailVerificationPurpose.SET_INITIAL_PASSWORD);
        token.UsedAt.Should().BeNull();
        _dbFactory.EmailService.SentAccountCreatedLinks.Should().ContainSingle(x => x.To == email && x.Info.UserId == adminUserId);

        var activityLog = await db.ActivityLogs.SingleAsync(x => x.UserId == SystemAdminId && x.Action == ActivityLogAction.CREATE_OPERATOR);
        using var metadata = JsonDocument.Parse(activityLog.Metadata!);
        metadata.RootElement.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        metadata.RootElement.GetProperty("actorUserId").GetGuid().Should().Be(SystemAdminId);
        metadata.RootElement.GetProperty("targetUserId").GetGuid().Should().Be(adminUserId);
        metadata.RootElement.GetProperty("source").GetString().Should().Be("SYSTEM_ADMIN_CREATE_OPERATOR");
    }

    [Theory]
    [InlineData("businessRegistrationNumber", "OPERATOR_DUPLICATE_REGISTRATION")]
    [InlineData("taxCode", "OPERATOR_DUPLICATE_TAX_CODE")]
    [InlineData("contactEmail", "AUTH_EMAIL_ALREADY_REGISTERED")]
    [InlineData("representativePhone", "AUTH_PHONE_ALREADY_REGISTERED")]
    public async Task Register_DuplicateOperatorOrAdminFields_ReturnsCanonicalConflictWithoutSideEffects(
        string duplicatedField,
        string expectedCode)
    {
        await _dbFactory.ResetAsync();
        var existingEmail = UniqueEmail("register-existing");
        var existingBrn = $"BRN-{Guid.NewGuid():N}";
        var existingTaxCode = $"TAX-{Guid.NewGuid():N}";
        const string ExistingAdminPhone = "+84901234568";
        using var client = _dbFactory.CreateIdempotentClient();

        var created = await client.PostAsJsonAsync(
            "/v1/operators/register",
            ValidRegisterPayload("existing", existingEmail, existingBrn, existingTaxCode, representativePhone: ExistingAdminPhone));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var countsBefore = await _dbFactory.CountSideEffectsAsync();
        var outboxCountBefore = await _dbFactory.CountOutboxEventsAsync();

        var duplicate = await client.PostAsJsonAsync(
            "/v1/operators/register",
            DuplicateRegisterPayload(duplicatedField, existingEmail, existingBrn, existingTaxCode, ExistingAdminPhone));

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var doc = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        AssertErrorEnvelope(doc, 409, expectedCode);
        (await _dbFactory.CountSideEffectsAsync()).Should().Be(countsBefore);
        // No new outbox events should be added for a failed registration.
        (await _dbFactory.CountOutboxEventsAsync()).Should().Be(outboxCountBefore);
    }

    [Theory]
    [InlineData("businessRegistrationNumber", "OPERATOR_DUPLICATE_REGISTRATION")]
    [InlineData("taxCode", "OPERATOR_DUPLICATE_TAX_CODE")]
    [InlineData("contactEmail", "AUTH_EMAIL_ALREADY_REGISTERED")]
    [InlineData("representativePhone", "AUTH_PHONE_ALREADY_REGISTERED")]
    public async Task AdminCreate_DuplicateOperatorOrAdminFields_ReturnsCanonicalConflictWithoutSideEffects(
        string duplicatedField,
        string expectedCode)
    {
        await _dbFactory.ResetAsync();
        await _dbFactory.SeedSystemAdminAsync(SystemAdminId);
        var existingEmail = UniqueEmail("admin-existing");
        var existingBrn = $"BRN-{Guid.NewGuid():N}";
        var existingTaxCode = $"TAX-{Guid.NewGuid():N}";
        const string ExistingAdminPhone = "+84901234568";
        using var client = _dbFactory.CreateIdempotentClient();
        using var createRequest = CreateAuthorizedAdminCreateRequest(
            ValidAdminCreatePayload("existing", existingEmail, existingBrn, existingTaxCode, representativePhone: ExistingAdminPhone));

        var created = await client.SendAsync(createRequest);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var countsBefore = await _dbFactory.CountSideEffectsAsync();
        var sentLinkCountBefore = _dbFactory.EmailService.SentAccountCreatedLinks.Count;
        using var duplicateRequest = CreateAuthorizedAdminCreateRequest(
            DuplicateAdminCreatePayload(duplicatedField, existingEmail, existingBrn, existingTaxCode, ExistingAdminPhone));

        var duplicate = await client.SendAsync(duplicateRequest);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var doc = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        AssertErrorEnvelope(doc, 409, expectedCode);
        (await _dbFactory.CountSideEffectsAsync()).Should().Be(countsBefore);
        _dbFactory.EmailService.SentAccountCreatedLinks.Should().HaveCount(sentLinkCountBefore);
    }

    private static object ValidRegisterPayload(
        string suffix,
        string? email = null,
        string? brn = null,
        string? taxCode = null,
        string? contactPhone = null,
        string? representativePhone = null)
        => new
        {
            name = "Operator Co",
            contactEmail = email ?? UniqueEmail($"register-{suffix}"),
            contactPhone = contactPhone ?? "+84901234567",
            businessRegistrationNumber = brn ?? $"BRN-{Guid.NewGuid():N}",
            taxCode = taxCode ?? $"TAX-{Guid.NewGuid():N}",
            addressStreet = "1 Street",
            addressWard = "Ward",
            addressDistrict = "District",
            addressProvince = "Province",
            representativeName = "Operator Admin",
            representativePhone = representativePhone ?? "+84901234568",
            password = "Password123!",
        };

    private static object DuplicateRegisterPayload(
        string duplicatedField,
        string existingEmail,
        string existingBrn,
        string existingTaxCode,
        string existingAdminPhone)
        => ValidRegisterPayload(
            "duplicate",
            email: duplicatedField == "contactEmail" ? existingEmail : UniqueEmail("register-duplicate"),
            brn: duplicatedField == "businessRegistrationNumber" ? existingBrn : $"BRN-{Guid.NewGuid():N}",
            taxCode: duplicatedField == "taxCode" ? existingTaxCode : $"TAX-{Guid.NewGuid():N}",
            representativePhone: duplicatedField == "representativePhone" ? existingAdminPhone : "+84901234569");

    private static HttpRequestMessage CreateAuthorizedAdminCreateRequest(object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/operators")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(SystemAdminId, UserRole.SYSTEM_ADMIN.ToString())}");

        return request;
    }

    private static object ValidAdminCreatePayload(
        string suffix,
        string? email = null,
        string? brn = null,
        string? taxCode = null,
        string? contactPhone = null,
        string? representativePhone = null)
        => new
        {
            name = "Operator Co",
            contactEmail = email ?? UniqueEmail($"operator-{suffix}"),
            contactPhone = contactPhone ?? "+84901234567",
            businessRegistrationNumber = brn ?? $"BRN-{Guid.NewGuid():N}",
            taxCode = taxCode ?? $"TAX-{Guid.NewGuid():N}",
            addressStreet = "1 Street",
            addressWard = "Ward",
            addressDistrict = "District",
            addressProvince = "Province",
            representativeName = "Operator Admin",
            representativePhone = representativePhone ?? "+84901234568",
        };

    private static object DuplicateAdminCreatePayload(
        string duplicatedField,
        string existingEmail,
        string existingBrn,
        string existingTaxCode,
        string existingAdminPhone)
        => ValidAdminCreatePayload(
            "duplicate",
            email: duplicatedField == "contactEmail" ? existingEmail : UniqueEmail("admin-duplicate"),
            brn: duplicatedField == "businessRegistrationNumber" ? existingBrn : $"BRN-{Guid.NewGuid():N}",
            taxCode: duplicatedField == "taxCode" ? existingTaxCode : $"TAX-{Guid.NewGuid():N}",
            representativePhone: duplicatedField == "representativePhone" ? existingAdminPhone : "+84901234569");

    private static string UniqueEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private static void AssertSuccessEnvelope(JsonDocument doc, int statusCode)
    {
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(statusCode);
        doc.RootElement.TryGetProperty("data", out _).Should().BeTrue();
        doc.RootElement.GetProperty("meta").TryGetProperty("traceId", out _).Should().BeTrue();
    }

    private static void AssertErrorEnvelope(JsonDocument doc, int statusCode, string code)
    {
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(statusCode);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
        doc.RootElement.GetProperty("meta").TryGetProperty("traceId", out _).Should().BeTrue();
    }

    private static string CreateInternalJwt(Guid userId, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthWebApplicationFactory.InternalJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("role", role),
                new Claim(ClaimTypes.Role, role),
            ],
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: DateTime.UtcNow.AddSeconds(120),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public sealed record DbSideEffectCounts(
        int Operators,
        int Users,
        int OperatorSubscriptions,
        int EmailVerificationTokens,
        int ActivityLogs);

    public sealed class DbBackedOperatorsFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString = BuildTestDatabaseConnectionString();
        private readonly string _databaseName;
        private bool _databaseCreated;
        private bool _initialized;

        public DbBackedOperatorsFactory()
        {
            _databaseName = new NpgsqlConnectionStringBuilder(_connectionString).Database!;
        }

        public CapturingEmailService EmailService { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", AuthWebApplicationFactory.InternalJwtSecret);
            builder.UseEnvironment("Testing");
            builder.UseSetting("INTERNAL_JWT_SECRET", AuthWebApplicationFactory.InternalJwtSecret);
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("REDIS_URL", "localhost:6379,abortConnect=false");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.AddScoped(sp => new DbContextOptionsBuilder<IdentityDbContext>()
                    .EnableServiceProviderCaching(false)
                    .ConfigureWarnings(warnings => warnings.Ignore(
                        Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
                    .UseNpgsql(
                        sp.GetRequiredService<NpgsqlDataSource>(),
                        npgsql => npgsql.MigrationsHistoryTable(
                            "__ef_migrations_history",
                            IdentityDbContext.SchemaName))
                    .Options);
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(EmailService);
            });
        }

        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                return;
            }

            if (!_databaseCreated)
            {
                await CreateDatabaseAsync();
            }

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.MigrateAsync();
            await ReloadPostgresTypesAsync();
            _initialized = true;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await DropDatabaseAsync();
        }

        public async Task ResetAsync()
        {
            await InitializeAsync();
            EmailService.SentAccountCreatedLinks.Clear();

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE vietride_identity.activity_logs, vietride_identity.email_verification_tokens, vietride_identity.operator_subscriptions, vietride_identity.users, vietride_identity.operators, vietride_identity.outbox_events RESTART IDENTITY CASCADE;");
        }

        public async Task<DbSideEffectCounts> CountSideEffectsAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            return new DbSideEffectCounts(
                await db.Operators.CountAsync(),
                await db.Users.CountAsync(),
                await db.OperatorSubscriptions.CountAsync(),
                await db.EmailVerificationTokens.CountAsync(),
                await db.ActivityLogs.CountAsync());
        }

        public async Task<int> CountOutboxEventsAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            return await db.Set<OutboxEvent>().CountAsync();
        }

        public async Task SeedSystemAdminAsync(Guid userId)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var systemAdmin = User.CreateAdminPendingPassword("system-admin@example.com", "System Admin");
            SetPrivateProperty(systemAdmin, nameof(User.Id), userId);
            await db.Users.AddAsync(systemAdmin);
            await db.SaveChangesAsync();
        }

        public async Task DropDatabaseAsync()
        {
            if (!_databaseCreated)
            {
                return;
            }

            await using var connection = new NpgsqlConnection(BuildMaintenanceConnectionString());
            await connection.OpenAsync();
            await using var terminateCommand = connection.CreateCommand();
            terminateCommand.CommandText =
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @databaseName AND pid <> pg_backend_pid();";
            terminateCommand.Parameters.AddWithValue("databaseName", _databaseName);
            await terminateCommand.ExecuteNonQueryAsync();

            await using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
            await dropCommand.ExecuteNonQueryAsync();
            _databaseCreated = false;
        }

        private async Task CreateDatabaseAsync()
        {
            await using var connection = new NpgsqlConnection(BuildMaintenanceConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
            await command.ExecuteNonQueryAsync();
            _databaseCreated = true;
        }

        private async Task ReloadPostgresTypesAsync()
        {
            var dataSource = Services.GetRequiredService<NpgsqlDataSource>();
            await using var connection = await dataSource.OpenConnectionAsync();
            await connection.ReloadTypesAsync();
        }

        private string BuildMaintenanceConnectionString()
        {
            var builder = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                Database = "postgres",
            };

            return builder.ConnectionString;
        }

        private static string BuildTestDatabaseConnectionString()
        {
            var configured = Environment.GetEnvironmentVariable("VIETRIDE_IDENTITY_TEST_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                ?? "Host=localhost;Port=5432;Database=vietride_identity_tests;Username=vietride;Password=vietride_dev";
            var builder = new NpgsqlConnectionStringBuilder(configured)
            {
                Database = $"vietride_identity_task6_1_{Guid.NewGuid():N}",
            };

            return builder.ConnectionString;
        }

        private static void SetPrivateProperty<T>(object entity, string propertyName, T value)
        {
            var type = entity.GetType();
            while (type is not null)
            {
                var property = type.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property is not null)
                {
                    property.SetValue(entity, value);
                    return;
                }

                type = type.BaseType;
            }

            throw new InvalidOperationException($"Property {propertyName} was not found on {entity.GetType().Name}.");
        }
    }

    public sealed class CapturingEmailService : IEmailService
    {
        public List<(string To, AccountCreatedEmailDto Info)> SentAccountCreatedLinks { get; } = [];

        public Task SendAccountCreatedLinkAsync(
            string to,
            AccountCreatedEmailDto accountInfo,
            CancellationToken ct = default)
        {
            SentAccountCreatedLinks.Add((to, accountInfo));
            return Task.CompletedTask;
        }

        public Task SendParcelDeliveryLinkAsync(
            string to,
            string deliveryToken,
            ParcelDeliveryEmailDto parcelInfo,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingSender : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)Handle(request));
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult<object?>(Handle(request));
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Operator endpoint tests do not use streaming MediatR requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Operator endpoint tests do not use streaming MediatR requests.");

        private static object Handle(object request)
            => request switch
            {
                RegisterOperatorCommand => new RegisterOperatorResponseDto(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Đơn đăng ký đã nhận, vui lòng xác thực email"),

                CreateOperatorCommand command => new CreateOperatorResponseDto(
                    new OperatorSummaryDto(
                        Guid.Parse("33333333-3333-3333-3333-333333333333"),
                        command.Name,
                        OperatorRegistrationStatus.APPROVED.ToString(),
                        command.ContactEmail,
                        command.ContactPhone,
                        command.BusinessRegistrationNumber,
                        command.TaxCode),
                    new OperatorAdminSummaryDto(
                        Guid.Parse("44444444-4444-4444-4444-444444444444"),
                        command.ContactEmail,
                        command.RepresentativePhone,
                        command.RepresentativeName,
                        UserRole.OPERATOR_ADMIN.ToString(),
                        UserStatus.PENDING_INITIAL_PASSWORD.ToString()),
                    new OperatorSubscriptionSummaryDto(
                        Guid.Parse("55555555-5555-5555-5555-555555555555"),
                        Guid.Parse("00000000-0000-0000-0000-000000000001"),
                        "Starter (Free Trial)",
                        SubscriptionStatus.ACTIVE.ToString(),
                        DateTimeOffset.Parse("2026-06-07T01:00:00Z"),
                        DateTimeOffset.Parse("2026-07-07T01:00:00Z"),
                        1)),

                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}."),
            };
    }
}
