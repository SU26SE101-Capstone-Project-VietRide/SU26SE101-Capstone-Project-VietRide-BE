using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Users.CompleteProfile;
using VietRide.Identity.Application.Features.Users.GetMe;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class UsersEndpointsTests : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly Guid CallerUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly AuthWebApplicationFactory _factory;

    public UsersEndpointsTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetMe_HappyPath_Returns200EnvelopeWithCallerProfile()
    {
        using var client = CreateClientWithSender(new HappyPathUsersSender());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/users/me");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(CallerUserId, "PASSENGER")}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 200);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("id").GetGuid().Should().Be(CallerUserId);
        data.GetProperty("email").GetString().Should().Be("user@example.com");
        data.GetProperty("displayName").GetString().Should().Be("Test User");
        data.GetProperty("phone").GetString().Should().Be("+84901234567");
        data.GetProperty("role").GetString().Should().Be("PASSENGER");
        data.GetProperty("status").GetString().Should().Be("ACTIVE");
    }

    [Fact]
    public async Task GetMe_WithoutAuth_Returns401()
    {
        using var client = CreateClientWithSender(new HappyPathUsersSender());

        var response = await client.GetAsync("/v1/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CompleteProfile_HappyPath_Returns200Envelope()
    {
        using var client = CreateClientWithSender(new HappyPathUsersSender());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/users/me/complete-profile")
        {
            Content = JsonContent.Create(new { phone = "+84901234567" }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(CallerUserId, "PASSENGER")}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 200);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("userId").GetGuid().Should().Be(CallerUserId);
        data.GetProperty("phone").GetString().Should().Be("+84901234567");
        data.GetProperty("message").GetString().Should().Be("Hồ sơ hoàn tất.");
    }

    [Fact]
    public async Task CompleteProfile_InvalidPhone_Returns400AuthPhoneInvalidFormat()
    {
        using var client = CreateClientWithRepositories();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/users/me/complete-profile")
        {
            Content = JsonContent.Create(new { phone = "not-a-phone" }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(CallerUserId, "PASSENGER")}");

        var response = await client.SendAsync(request);

        await AssertErrorCodeAsync(response, HttpStatusCode.BadRequest, "AUTH_PHONE_INVALID_FORMAT");
    }

    [Fact]
    public async Task CompleteProfile_EmptyPhone_Returns400AuthPhoneInvalidFormat()
    {
        using var client = CreateClientWithRepositories();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/users/me/complete-profile")
        {
            Content = JsonContent.Create(new { phone = string.Empty }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(CallerUserId, "PASSENGER")}");

        var response = await client.SendAsync(request);

        await AssertErrorCodeAsync(response, HttpStatusCode.BadRequest, "AUTH_PHONE_INVALID_FORMAT");
    }

    [Fact]
    public async Task CompleteProfile_MissingPhone_Returns400AuthPhoneInvalidFormat()
    {
        using var client = CreateClientWithRepositories();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/users/me/complete-profile")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(CallerUserId, "PASSENGER")}");

        var response = await client.SendAsync(request);

        await AssertErrorCodeAsync(response, HttpStatusCode.BadRequest, "AUTH_PHONE_INVALID_FORMAT");
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

    private HttpClient CreateClientWithRepositories()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUserRepository>();
                services.RemoveAll<IActivityLogRepository>();
                services.AddSingleton<IUserRepository>(new TestUsersRepository());
                services.AddSingleton<IActivityLogRepository>(new TestActivityLogRepository());
            });
        }).CreateClient();
    }

    private static async Task AssertErrorCodeAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string expectedErrorCode)
    {
        response.StatusCode.Should().Be(expectedStatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be((int)expectedStatusCode);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(expectedErrorCode);
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

    private sealed class TestUsersRepository : IUserRepository
    {
        private readonly User _user = User.CreateGoogleAccount("user@example.com", "Test User", null);

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<User?>(_user);

        public Task<User> AddAsync(User entity, CancellationToken ct)
            => Task.FromResult(entity);

        public void Update(User entity)
        {
        }

        public void Remove(User entity)
        {
        }

        public IQueryable<User> Query()
            => new[] { _user }.AsQueryable();

        public IQueryable<User> QueryNoTracking()
            => Query();

        public Task<User?> GetByEmailAsync(string emailLower, CancellationToken ct = default)
            => Task.FromResult<User?>(null);

        public Task<User?> GetByPhoneAsync(string e164Phone, CancellationToken ct = default)
            => Task.FromResult<User?>(null);
    }

    private sealed class TestActivityLogRepository : IActivityLogRepository
    {
        public Task<ActivityLog?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<ActivityLog?>(null);

        public Task<ActivityLog> AddAsync(ActivityLog entity, CancellationToken ct)
            => Task.FromResult(entity);

        public void Update(ActivityLog entity)
        {
        }

        public void Remove(ActivityLog entity)
        {
        }

        public IQueryable<ActivityLog> Query()
            => Array.Empty<ActivityLog>().AsQueryable();

        public IQueryable<ActivityLog> QueryNoTracking()
            => Query();
    }

    private sealed class HappyPathUsersSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => Task.FromResult((TResponse)Handle(request));

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(Handle(request));

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("User endpoint tests do not use streaming MediatR requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("User endpoint tests do not use streaming MediatR requests.");

        private static object Handle(object request)
            => request switch
            {
                GetMeQuery query => new GetMeResponseDto(
                    Id: query.UserId,
                    Email: "user@example.com",
                    DisplayName: "Test User",
                    Phone: "+84901234567",
                    Role: "PASSENGER",
                    OperatorId: null,
                    Status: "ACTIVE",
                    AvatarUrl: null),

                CompleteProfileCommand command when command.Phone == "not-a-phone"
                    => throw new BadRequestException("AUTH_PHONE_INVALID_FORMAT", "Invalid phone number format."),

                CompleteProfileCommand command => new CompleteProfileResponseDto(
                    UserId: command.UserId,
                    Phone: command.Phone!,
                    Message: "Hồ sơ hoàn tất."),

                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}."),
            };
    }
}
