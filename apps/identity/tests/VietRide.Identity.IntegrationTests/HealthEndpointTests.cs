using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VietRide.Identity.IntegrationTests;

/// Smoke tests for /health (liveness) and /v1/ping (Wave A controller).
/// Wires WebApplicationFactory<Program> with the minimum env required by AddVietRideSharedWeb +
/// AddVietRideDbContext (INTERNAL_JWT_SECRET ≥32 chars + a placeholder Default connection string —
/// /health does NOT touch the DB so an unreachable connection is fine for liveness only).
public class HealthEndpointTests : IClassFixture<VietRideWebApplicationFactory>
{
    private readonly VietRideWebApplicationFactory _factory;

    public HealthEndpointTests(VietRideWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_Returns200_WithServiceName()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Identity");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("ok");
        doc.RootElement.GetProperty("service").GetString().Should().Be("Identity");
    }

    [Fact]
    public async Task GetPing_Returns200_WithServiceName()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("service").GetString().Should().Be("Identity");
    }
}

public class VietRideWebApplicationFactory : WebApplicationFactory<Program>
{
    // Dev-only RSA 2048 private key (PKCS#8 PEM) — same placeholder as appsettings.Development.json.
    private const string DevPrivateKeyPem =
        "-----BEGIN PRIVATE KEY-----\n" +
        "MIIEuwIBADANBgkqhkiG9w0BAQEFAASCBKUwggShAgEAAoIBAQC+6Nk4TLBS4Hm3\n" +
        "p3/urqAAa+/eC1o+W4sbvmKEv2mZb9kxnTWwGudixb3bIxTD/5b468eI3cBftXZB\n" +
        "NMkgUBIeqC2KwYXdLE5uiDuhRTBNo21cY9mWRA9UocYiW8zEegoPevj9sbIvWATG\n" +
        "hvLVwkqi4j0UZhEwG7fmXKeJuGZfFGUXjnfKscNTVnV6hxcvtz9Txa9IgZdJyICr\n" +
        "Tk+MGh+qkrnt6iK3gx6NYufY9S+6ZkV0qA9tmLBVWMXAUg/VnNhRcfUbRM1HQLmG\n" +
        "HnC6w1ttMzsc8sbOI8Xt3/EXQDQaJjJfWgvaa1CnQx/AJz9co/qansxHFP37GueL\n" +
        "DAl0Th6BAgMBAAECgf9R7bLtx8z7Cf2PaqQrBIAaOdsITKhiMbiM3gSYIhjGtLle\n" +
        "EWsEkUFeitspmMGiFaU0ucxQh5QS8zXYUZS5Dxgr+KOSxcAtB8r+GNYJ9vjPcBkV\n" +
        "9fJ2le1EKoYAIXycGtrZVYQoct+zt3sPWsNQVhzgPnz38qb5T9SntkowCnLNK7R7\n" +
        "REXlJcRs2pOePukpKmFJwttZEmIkMv1zk1xmj0uAmM/SRr/Vir11d9uvz5UcRGYL\n" +
        "N+ig7qyQw7NSyv71EubDvvPcunM88whZ9oyTCQE7JcQBpq9QQ5+PBtEKLFv9Qddh\n" +
        "D9YT/Ys52hI92zhQNoi9+UTUpRlU6K8To2ZXr2cCgYEA+ASMtmTZU0giIi3zAl2P\n" +
        "5ysLwIXZzTmgPZXttCTfDNpqxaEY0tGLEP06JsPx5Us/bhLRmpQDgXwgMe8J6fi9\n" +
        "jk+n6rfoeTed3VhOAGGcABThzGU325JiCdIMpPMlrGsltisrgj8WJEdZYhtqx8W/\n" +
        "sCg/GWIe3+Qz7ceJSaYtXf8CgYEAxQ3G6IcTlZSdEosqTy/RWdsUDCmqo8EOpEgQ\n" +
        "cReN+Vq8JAwAz0UlABwee6na8dwRAGN4uaDdPf/q9NgTZhm3sBArFV0B/sVOIbNi\n" +
        "hH21136ER7MNTMJIm5TbNs2X9VoaZ94xAFSqrncj1PBsYL1jvvQaZ2h/p7q8kqJ0\n" +
        "nHlNg38CgYEAuWPNOtmPia01to7aQz5kvstycWqcL8ePe/mCQVH+WME7ZpbQ02VG\n" +
        "qmBfA3McceUZeNIgU4eoRzXdavXfV0FTj/kC73ShFVr5aecEB0zvKzBwyDQw2LRH\n" +
        "DEgyo2oNEyDUg6MpVqaJiny615be7o1mh+rNn8+0fG88UdUBTkglSUkCgYArfukC\n" +
        "9p3qDI3HRBSougNZ9DOuo5vY3Ypf1NBcRji+a7rPsh6Toc2TAqHv5gRAErVmAo7p\n" +
        "Woq7Xrv8I53UkaSsJkV8R7VjCSY/5hq+6Ai1cmW8dddftBrWzLq+lA8QxzzA5Jio\n" +
        "XAf4zq+IFzG1ANj9k2AopzZWTa/GJjnbOCNV/QKBgGm41nw1NY1mHvxTZOPwxvqL\n" +
        "ahIYVtjVZqKLkFhpfQ8rDziyjJMenOJrN1bNVFr5rp8qJooYgA7U8PUqFpznyMOy\n" +
        "a8Mnqco+K0o4Y25lSbqJBiE4uob0HBEHuRxtAKJGaT3S6uBQVrshFAnrUE9YttEh\n" +
        "3G9IZTcD9Xf6wKkFCbum\n" +
        "-----END PRIVATE KEY-----";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Provide just enough config to let Program.cs boot. /health is wired before auth
        // and does not exercise the DB, so an unreachable Postgres URL is acceptable here.
        Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", "test-secret-at-least-32-chars-long-xxxxx");
        builder.UseSetting("INTERNAL_JWT_SECRET", "test-secret-at-least-32-chars-long-xxxxx");
        builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
        builder.UseSetting("REDIS_URL", "localhost:6379,abortConnect=false");
        // RS256 keypair required by AddInfrastructure (JwksProvider + RsaAccessTokenService).
        builder.UseSetting("IdentityJwt:Kid", "test-kid");
        builder.UseSetting("IdentityJwt:PrivateKey", DevPrivateKeyPem);
        builder.UseEnvironment("Testing");
    }
}
