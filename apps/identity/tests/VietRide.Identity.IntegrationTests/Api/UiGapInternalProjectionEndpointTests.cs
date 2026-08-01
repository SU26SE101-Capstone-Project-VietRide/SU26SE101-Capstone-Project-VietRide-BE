using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using VietRide.Identity.Api.Controllers;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.InternalUsers.GetInternalUser;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Web.Authentication;
using VietRide.Shared.Web.Filters;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class UiGapInternalProjectionEndpointTests
{
    private const string InternalJwtSecret = "identity-internal-test-secret-32-chars";

    [Fact]
    public async Task BatchEndpoints_ReturnRawAdditiveProjectionAndRedactedDeletedUser()
    {
        var active = User.CreateOperatorScopedPendingPassword(
            "active@example.com",
            PhoneNumber.Parse("+84901234567"),
            "Active User",
            UserRole.DRIVER,
            Guid.NewGuid());
        var deleted = User.CreateOperatorScopedPendingPassword(
            "deleted@example.com",
            PhoneNumber.Parse("+84907654321"),
            "Deleted User",
            UserRole.ASSISTANT,
            Guid.NewGuid());
        deleted.SoftDelete(DateTimeOffset.UtcNow);
        var operatorTenant = Operator.CreatePending(
            "Nha xe A",
            "BR-001",
            "TAX-001",
            "ops@example.com",
            "+84901111111");
        var users = UserRepositoryProxy.Create([active, deleted]);
        var operators = OperatorRepositoryProxy.Create([operatorTenant]);
        await using var app = await CreateAppAsync(users, operators);
        using var client = app.GetTestClient();
        AddInternalJwt(client);

        var usersResponse = await client.GetAsync(
            $"/internal/v1/users?ids={active.Id}&ids={deleted.Id}");
        var operatorsResponse = await client.PostAsJsonAsync(
            "/internal/v1/operators/summaries/batch",
            new { operatorIds = new[] { operatorTenant.Id } });

        usersResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var usersJson = await usersResponse.Content.ReadAsStringAsync();
        usersJson.Should().NotContain("\"success\"");
        using (var document = JsonDocument.Parse(usersJson))
        {
            document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            var rows = document.RootElement.EnumerateArray().ToArray();
            rows.Should().HaveCount(2);
            rows[0].GetProperty("displayName").GetString().Should().Be("Active User");
            rows[0].GetProperty("phone").GetString().Should().Be("+84901234567");
            rows[1].GetProperty("displayName").GetString().Should().Be("Người dùng đã xóa");
            rows[1].GetProperty("phone").ValueKind.Should().Be(JsonValueKind.Null);
            rows[1].GetProperty("deleted").GetBoolean().Should().BeTrue();
        }

        operatorsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var document = JsonDocument.Parse(await operatorsResponse.Content.ReadAsStringAsync()))
        {
            var row = document.RootElement.EnumerateArray().Single();
            row.GetProperty("operatorName").GetString().Should().Be("Nha xe A");
            row.GetProperty("contactPhone").GetString().Should().Be("+84901111111");
            row.TryGetProperty("logoUrl", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task UserBatch_WithoutInternalJwt_Returns401()
    {
        await using var app = await CreateAppAsync(
            UserRepositoryProxy.Create([]),
            OperatorRepositoryProxy.Create([]));

        var response = await app.GetTestClient().GetAsync(
            $"/internal/v1/users?ids={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<WebApplication> CreateAppAsync(
        IUserRepository users,
        IOperatorRepository operators)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddControllers(options =>
            {
                options.Filters.Add<ApiResponseExceptionFilter>();
                options.Filters.Add<ApiResponseResultFilter>();
            })
            .AddApplicationPart(typeof(InternalUsersController).Assembly);
        builder.Services.AddAuthentication(InternalJwtAuthenticationExtensions.Scheme)
            .AddInternalJwt(InternalJwtSecret);
        builder.Services.AddAuthorization();
        builder.Services.AddMediatR(typeof(GetInternalUsersQueryHandler).Assembly);
        builder.Services.AddSingleton(users);
        builder.Services.AddSingleton(operators);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static void AddInternalJwt(HttpClient client)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(InternalJwtSecret));
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, "ui-gap-test")],
            notBefore: now.AddSeconds(-5),
            expires: now.AddSeconds(120),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Add(
            InternalJwtAuthenticationExtensions.HeaderName,
            $"Bearer {new JwtSecurityTokenHandler().WriteToken(token)}");
    }

    private class UserRepositoryProxy : DispatchProxy
    {
        private IReadOnlyList<User> _users = [];

        public static IUserRepository Create(IReadOnlyList<User> users)
        {
            var proxy = Create<IUserRepository, UserRepositoryProxy>();
            ((UserRepositoryProxy)(object)proxy)._users = users;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "ListByIdsIncludingDeletedAsync")
            {
                var ids = (IReadOnlyCollection<Guid>)args![0]!;
                return Task.FromResult<IReadOnlyList<User>>(
                    _users.Where(user => ids.Contains(user.Id)).ToArray());
            }

            throw new NotSupportedException($"Unexpected repository call: {targetMethod?.Name}");
        }
    }

    private class OperatorRepositoryProxy : DispatchProxy
    {
        private IReadOnlyList<Operator> _operators = [];

        public static IOperatorRepository Create(IReadOnlyList<Operator> operators)
        {
            var proxy = Create<IOperatorRepository, OperatorRepositoryProxy>();
            ((OperatorRepositoryProxy)(object)proxy)._operators = operators;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "ListSummariesByIdsAsync")
            {
                var ids = (IReadOnlyCollection<Guid>)args![0]!;
                return Task.FromResult<IReadOnlyList<Operator>>(
                    _operators.Where(operatorTenant => ids.Contains(operatorTenant.Id)).ToArray());
            }

            throw new NotSupportedException($"Unexpected repository call: {targetMethod?.Name}");
        }
    }
}
