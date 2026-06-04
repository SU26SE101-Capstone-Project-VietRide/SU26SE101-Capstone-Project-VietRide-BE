using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Identity.Application.Features.Admin.CreateAdminUser;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class AdminUsersEndpointsTests : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly Guid SystemAdminId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PassengerId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly AuthWebApplicationFactory _factory;

    public AdminUsersEndpointsTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateAdminUser_HappyPath_Returns201Envelope()
    {
        using var client = CreateClientWithSender(new HappyPathAdminUsersSender());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/users")
        {
            Content = JsonContent.Create(new
            {
                email = "new-admin@example.com",
                displayName = "New Admin",
                role = "SYSTEM_ADMIN",
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(SystemAdminId, "SYSTEM_ADMIN")}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 201);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("email").GetString().Should().Be("new-admin@example.com");
        data.GetProperty("displayName").GetString().Should().Be("New Admin");
        data.GetProperty("role").GetString().Should().Be("SYSTEM_ADMIN");
        data.GetProperty("status").GetString().Should().Be("PENDING_INITIAL_PASSWORD");
    }

    [Fact]
    public async Task CreateAdminUser_NonSystemAdminCaller_Returns403ForbiddenEnvelope()
    {
        using var client = CreateClientWithSender(new HappyPathAdminUsersSender());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/users")
        {
            Content = JsonContent.Create(new
            {
                email = "new-admin@example.com",
                displayName = "New Admin",
                role = "SYSTEM_ADMIN",
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(PassengerId, "PASSENGER")}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(403);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("FORBIDDEN");
    }

    private HttpClient CreateClientWithSender(ISender sender)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISender>();
                services.RemoveAll<IMediator>();
                services.AddSingleton(sender);
            });
        }).CreateClient();
    }

    private static void AssertSuccessEnvelope(JsonDocument doc, int expectedStatusCode)
    {
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(expectedStatusCode);
        doc.RootElement.TryGetProperty("data", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("meta", out _).Should().BeTrue();
    }

    private static string CreateInternalJwt(Guid userId, string role)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthWebApplicationFactory.InternalJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("role", role),
            ],
            notBefore: now.AddSeconds(-5),
            expires: now.AddSeconds(120),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class HappyPathAdminUsersSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => Task.FromResult((TResponse)Handle(request));

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(Handle(request));

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Admin user endpoint tests do not use streaming MediatR requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Admin user endpoint tests do not use streaming MediatR requests.");

        private static object Handle(object request)
            => request switch
            {
                CreateAdminUserCommand command => new CreateAdminUserResponseDto(
                    UserId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    Email: command.Email,
                    DisplayName: command.DisplayName,
                    Role: UserRole.SYSTEM_ADMIN.ToString(),
                    Status: UserStatus.PENDING_INITIAL_PASSWORD.ToString()),

                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}."),
            };
    }
}
