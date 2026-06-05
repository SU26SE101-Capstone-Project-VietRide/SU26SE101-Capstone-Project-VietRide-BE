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
using Microsoft.IdentityModel.Tokens;
using VietRide.Identity.Api.Controllers;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Devices.GetActiveDeviceTokens;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Web.Authentication;

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
        Assert.Contains("active-android-token", json, StringComparison.Ordinal);
        Assert.Contains("active-ios-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("inactive-ios-token", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDeviceTokens_WithoutInternalJwt_ReturnsUnauthorized()
    {
        await using var app = await CreateAppAsync(Array.Empty<DeviceRow>());
        using var client = app.GetTestClient();

        var response = await client.GetAsync($"/internal/v1/users/{Guid.NewGuid()}/device-tokens");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<WebApplication> CreateAppAsync(IReadOnlyList<DeviceRow> devices)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(InternalUsersController).Assembly);
        builder.Services.AddAuthentication(InternalJwtAuthenticationExtensions.Scheme)
            .AddInternalJwt(InternalJwtSecret);
        builder.Services.AddAuthorization();
        builder.Services.AddMediatR(typeof(GetActiveDeviceTokensQueryHandler).Assembly);
        builder.Services.AddSingleton<IUserDeviceRepository>(UserDeviceRepositoryProxy.Create(devices));

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
