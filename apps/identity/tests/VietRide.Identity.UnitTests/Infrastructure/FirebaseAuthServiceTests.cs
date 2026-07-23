using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using VietRide.Identity.Infrastructure.ExternalClients;

namespace VietRide.Identity.UnitTests.Infrastructure;

public sealed class FirebaseAuthServiceTests
{
    [Fact]
    public async Task CreateCustomToken_ContainsUidRolePurposeAndOneHourLifetime()
    {
        using var rsa = RSA.Create(2048);
        var options = new FirebaseAuthOptions
        {
            ProjectId = "vietride-test",
            ClientEmail = "firebase-adminsdk@vietride-test.iam.gserviceaccount.com",
            PrivateKey = rsa.ExportPkcs8PrivateKeyPem(),
        };
        var service = new FirebaseAuthService(options);
        var userId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        var token = await service.CreateCustomTokenAsync(
            userId,
            "OPERATOR_ADMIN",
            operatorId,
            "VEHICLE_IMAGE",
            CancellationToken.None);

        using var payload = JsonDocument.Parse(DecodeJwtSegment(token.Split('.')[1]));
        var root = payload.RootElement;
        root.GetProperty("uid").GetString().Should().Be(userId.ToString("D"));
        var claims = root.GetProperty("claims");
        claims.GetProperty("operatorId").GetString().Should().Be(operatorId.ToString("D"));
        claims.GetProperty("role").GetString().Should().Be("OPERATOR_ADMIN");
        claims.GetProperty("uploadPurpose").GetString().Should().Be("VEHICLE_IMAGE");
        var lifetimeSeconds = root.GetProperty("exp").GetInt64() - root.GetProperty("iat").GetInt64();
        lifetimeSeconds.Should().Be(3600);
    }

    private static byte[] DecodeJwtSegment(string segment)
    {
        var value = segment.Replace('-', '+').Replace('_', '/');
        value = value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '=');
        return Convert.FromBase64String(value);
    }
}
