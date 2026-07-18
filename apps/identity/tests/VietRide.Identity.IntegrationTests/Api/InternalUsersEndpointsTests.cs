using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using VietRide.Identity.Api.Controllers;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Devices.GetActiveDeviceTokens;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Web.Authentication;
using VietRide.Shared.Web.Filters;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class InternalUsersEndpointsTests
{
    private const string InternalJwtSecret = "identity-internal-test-secret-32-chars";

    [Fact]
    public async Task GetDeviceTokens_WithInternalJwt_ReturnsActiveDeviceTokensOnly()
    {
        await using var app = await CreateAppAsync(new[]
        {
            DeviceRow.Active("active-android-token", "ANDROID"),
            DeviceRow.Inactive("inactive-ios-token", "IOS"),
            DeviceRow.Active("active-ios-token", "IOS"),
        });
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(InternalJwtAuthenticationExtensions.HeaderName, $"Bearer {CreateInternalJwt()}");

        var response = await client.GetAsync($"/internal/v1/users/{Guid.NewGuid()}/device-tokens");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.DoesNotContain("\"success\"", json, StringComparison.Ordinal);
        Assert.Contains("active-android-token", json, StringComparison.Ordinal);
        Assert.Contains("active-ios-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("inactive-ios-token", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUser_WithInternalJwt_ReturnsRawUserLookupDto()
    {
        var operatorId = Guid.NewGuid();
        var user = User.CreateOperatorScopedPendingPassword(
            "driver@example.com",
            PhoneNumber.Parse("+84901234567"),
            "Driver One",
            UserRole.DRIVER,
            operatorId);
        await using var app = await CreateAppAsync([], [user]);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(InternalJwtAuthenticationExtensions.HeaderName, $"Bearer {CreateInternalJwt()}");

        var response = await client.GetAsync($"/internal/v1/users/{user.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("success", out _));
        Assert.Equal(user.Id, doc.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("DRIVER", doc.RootElement.GetProperty("role").GetString());
        Assert.Equal(operatorId, doc.RootElement.GetProperty("operatorId").GetGuid());
        Assert.Equal("PENDING_INITIAL_PASSWORD", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetUserByPhone_WithInternalJwt_ReturnsRawUserId()
    {
        var user = User.CreateOperatorScopedPendingPassword(
            "phone@example.com", PhoneNumber.Parse("+84901234567"), "Phone User", UserRole.DRIVER, Guid.NewGuid());
        await using var app = await CreateAppAsync([], [user]);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(InternalJwtAuthenticationExtensions.HeaderName, $"Bearer {CreateInternalJwt()}");

        var response = await client.GetAsync("/internal/v1/users/by-phone?phone=%2B84901234567");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.TryGetProperty("success", out _));
        Assert.Equal(user.Id, doc.RootElement.GetProperty("userId").GetGuid());
        Assert.Single(doc.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task GetUserByPhone_NoMatch_ReturnsResourceNotFoundEnvelope()
    {
        await using var app = await CreateAppAsync([]);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(InternalJwtAuthenticationExtensions.HeaderName, $"Bearer {CreateInternalJwt()}");

        var response = await client.GetAsync("/internal/v1/users/by-phone?phone=%2B84901234567");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("RESOURCE_NOT_FOUND", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetUserByPhone_WithoutInternalJwt_ReturnsUnauthorized()
    {
        await using var app = await CreateAppAsync([]);
        var response = await app.GetTestClient().GetAsync("/internal/v1/users/by-phone?phone=%2B84901234567");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDeviceTokens_WithoutInternalJwt_ReturnsUnauthorizedEnvelope()
    {
        await using var app = await CreateAppAsync(Array.Empty<DeviceRow>());
        using var client = app.GetTestClient();

        var response = await client.GetAsync($"/internal/v1/users/{Guid.NewGuid()}/device-tokens");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(401, doc.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("AUTH_TOKEN_INVALID", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.True(doc.RootElement.TryGetProperty("meta", out _));
    }

    private static async Task<WebApplication> CreateAppAsync(
        IReadOnlyList<DeviceRow> devices,
        IReadOnlyList<User>? users = null)
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
        builder.Services.AddMediatR(typeof(GetActiveDeviceTokensQueryHandler).Assembly);
        builder.Services.AddSingleton<IUserDeviceRepository>(UserDeviceRepositoryProxy.Create(devices));
        builder.Services.AddSingleton<IUserRepository>(UserRepositoryProxy.Create(users ?? []));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        await app.StartAsync();
        return app;
    }

    private static string CreateInternalJwt()
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(InternalJwtSecret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: new[] { new Claim("sub", "notification-service") },
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: DateTime.UtcNow.AddSeconds(120),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record DeviceRow(string FcmToken, string Platform, bool IsActive)
    {
        public static DeviceRow Active(string fcmToken, string platform) => new(fcmToken, platform, true);

        public static DeviceRow Inactive(string fcmToken, string platform) => new(fcmToken, platform, false);
    }

    private class UserRepositoryProxy : DispatchProxy
    {
        private IReadOnlyList<User> _users = Array.Empty<User>();

        public static IUserRepository Create(IReadOnlyList<User> users)
        {
            var proxy = Create<IUserRepository, UserRepositoryProxy>();
            ((UserRepositoryProxy)(object)proxy)._users = users;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "GetByIdAsync")
            {
                var userId = (Guid)args![0]!;
                return Task.FromResult(_users.FirstOrDefault(user => user.Id == userId));
            }

            if (targetMethod?.Name == "GetByPhoneAsync")
            {
                var phone = (string)args![0]!;
                return Task.FromResult(_users.FirstOrDefault(user => user.Phone?.Value == phone));
            }

            if (targetMethod?.ReturnType == typeof(IQueryable<User>))
            {
                return _users.AsQueryable();
            }

            throw new NotSupportedException($"Unexpected repository call: {targetMethod?.Name}");
        }
    }

    private class UserDeviceRepositoryProxy : DispatchProxy
    {
        private IReadOnlyList<DeviceRow> _devices = Array.Empty<DeviceRow>();

        public static IUserDeviceRepository Create(IReadOnlyList<DeviceRow> devices)
        {
            var proxy = Create<IUserDeviceRepository, UserDeviceRepositoryProxy>();
            ((UserDeviceRepositoryProxy)(object)proxy)._devices = devices;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "ListActiveByUserIdAsync")
            {
                var activeDevices = _devices
                    .Where(device => device.IsActive)
                    .Select(device => CreateDevice(device.FcmToken, device.Platform))
                    .ToList();

                return Task.FromResult<IReadOnlyList<UserDevice>>(activeDevices);
            }

            throw new NotSupportedException($"Unexpected repository call: {targetMethod?.Name}");
        }
    }

    private static UserDevice CreateDevice(string fcmToken, string platform)
    {
        var device = (UserDevice)Activator.CreateInstance(typeof(UserDevice), nonPublic: true)!;
        SetProperty(device, "FcmToken", fcmToken);

        var platformProperty = typeof(UserDevice).GetProperty("Platform")!;
        var platformValue = Enum.Parse(platformProperty.PropertyType, platform, ignoreCase: true);
        SetProperty(device, "Platform", platformValue);

        return device;
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(propertyName)!;
        if (property.SetMethod is not null)
        {
            property.SetValue(instance, value);
            return;
        }

        var backingField = instance.GetType().GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        backingField.SetValue(instance, value);
    }
}
