using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure.DependencyInjection;
using VietRide.Identity.Infrastructure.Security;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Security;

public sealed class RsaAccessTokenServiceTests
{
    // Dev-only RSA 2048 private key (PKCS#8 PEM) — NOT a production key.
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

    private const string DevKid = "dev-2026-05";

    private static readonly PhoneNumber TestPhone = PhoneNumber.Parse("+84901234567");

    // A future timestamp so tokens remain within-lifetime for the test validation step.
    private static readonly DateTimeOffset FrozenNow =
        new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static IClock MakeFrozenClock(DateTimeOffset? at = null)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(at ?? FrozenNow);
        return clock;
    }

    private static RsaAccessTokenService CreateService(IClock? clock = null)
    {
        var opts = Options.Create(new JwtSigningOptions
        {
            PrivateKey = DevPrivateKeyPem,
            Kid = DevKid,
        });
        return new RsaAccessTokenService(opts, clock ?? MakeFrozenClock());
    }

    private static JwksProvider CreateJwksProvider()
    {
        var opts = Options.Create(new JwtSigningOptions
        {
            PrivateKey = DevPrivateKeyPem,
            Kid = DevKid,
        });
        return new JwksProvider(opts);
    }

    private static User MakeActivePassenger()
    {
        var user = User.CreatePassenger(
            "test@example.com",
            TestPhone,
            "$2a$12$hashedpassword",
            "Test User");

        user.VerifyEmail();
        return user;
    }

    // -------------------------------------------------------------------------
    // Happy-path
    // -------------------------------------------------------------------------

    [Fact]
    public void IssueToken_ReturnsNonEmptyJwtString()
    {
        var service = CreateService();
        var user = MakeActivePassenger();

        var token = service.IssueToken(user);

        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void IssueToken_TokenVerifiesWithPublicKey()
    {
        // Use wall-clock so nbf/exp are relative to now — validates signature + structure.
        var service = CreateService(new SystemClock());
        var user = MakeActivePassenger();

        var tokenStr = service.IssueToken(user);

        // Extract public key from the PEM to validate
        using var rsa = RSA.Create();
        rsa.ImportFromPem(DevPrivateKeyPem.AsSpan());
        var publicKey = new RsaSecurityKey(rsa) { KeyId = DevKid };

        var handler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            IssuerSigningKey = publicKey,
            ValidIssuer = "vietride-identity",
            ValidAudience = "vietride-api",
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };

        var act = () => handler.ValidateToken(tokenStr, validationParams, out _);
        act.Should().NotThrow();
    }

    [Fact]
    public void IssueToken_ContainsRequiredClaims()
    {
        var service = CreateService();
        var user = MakeActivePassenger();

        var tokenStr = service.IssueToken(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenStr);

        jwt.Issuer.Should().Be("vietride-identity");
        jwt.Claims.Should().Contain(c => c.Type == "sub" && c.Value == user.Id.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "role" && c.Value == UserRole.PASSENGER.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "email" && c.Value == user.Email);
        jwt.Header.Kid.Should().Be(DevKid);
    }

    [Fact]
    public void IssueToken_ExpiresExactly15MinutesAfterFrozenClock()
    {
        var clock = MakeFrozenClock(FrozenNow);
        var service = CreateService(clock);
        var user = MakeActivePassenger();

        var tokenStr = service.IssueToken(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenStr);

        jwt.ValidTo.Should().Be(FrozenNow.AddMinutes(15).UtcDateTime);
    }

    [Fact]
    public void IssueToken_PassengerToken_DoesNotContainOperatorIdClaim()
    {
        var service = CreateService();
        var user = MakeActivePassenger(); // PASSENGER — no OperatorId

        var tokenStr = service.IssueToken(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenStr);

        jwt.Claims.Should().NotContain(c => c.Type == "operatorId",
            because: "PASSENGER users have no operator; claim must be omitted, not set to empty");
    }

    [Fact]
    public void JwksProvider_ReturnsJsonWithCorrectShape()
    {
        var provider = CreateJwksProvider();

        var json = provider.GetJwks();

        json.Should().Contain("\"kty\":\"RSA\"");
        json.Should().Contain("\"alg\":\"RS256\"");
        json.Should().Contain("\"use\":\"sig\"");
        json.Should().Contain($"\"kid\":\"{DevKid}\"");
        json.Should().Contain("\"n\":");
        json.Should().Contain("\"e\":");
    }

    [Fact]
    public void JwksProvider_KidMatchesAccessTokenKid()
    {
        var service = CreateService();
        var provider = CreateJwksProvider();
        var user = MakeActivePassenger();

        var tokenStr = service.IssueToken(user);
        var jwks = provider.GetJwks();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenStr);

        jwks.Should().Contain($"\"kid\":\"{jwt.Header.Kid}\"");
    }

    // -------------------------------------------------------------------------
    // Refresh-token SHA-256 roundtrip
    // -------------------------------------------------------------------------

    [Fact]
    public void RefreshTokenFactory_TokenHashRoundtrip_LookupSucceeds()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var factory = new RefreshTokenFactory(clock);

        var (rawToken, entity) = factory.Create(Guid.NewGuid(), null, null);

        // Simulate DB lookup: re-compute hash from raw token
        var recomputedHash = RefreshTokenFactory.ComputeSha256Hex(rawToken);

        entity.TokenHash.Should().Be(recomputedHash);
        entity.TokenHash.Should().HaveLength(64); // SHA-256 hex = 64 chars
        entity.TokenHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void RefreshTokenFactory_NewFamily_GetsNewFamilyId()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var factory = new RefreshTokenFactory(clock);

        var userId = Guid.NewGuid();
        var (_, entity1) = factory.Create(userId, null, null);
        var (_, entity2) = factory.Create(userId, null, null);

        entity1.FamilyId.Should().NotBe(entity2.FamilyId);
    }

    [Fact]
    public void RefreshTokenFactory_Rotation_PreservesFamilyId()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var factory = new RefreshTokenFactory(clock);

        var userId = Guid.NewGuid();
        var (_, first) = factory.Create(userId, null, null);
        var (_, rotated) = factory.Create(userId, first.Id, first.FamilyId);

        rotated.FamilyId.Should().Be(first.FamilyId);
        rotated.ParentTokenId.Should().Be(first.Id);
    }

    // -------------------------------------------------------------------------
    // Options binding
    // -------------------------------------------------------------------------

    [Fact]
    public void AddInfrastructure_UserJwtEnvVars_OverrideIdentityJwtSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IdentityJwt:PrivateKey"] = "section-private-key",
                ["IdentityJwt:Kid"] = "section-kid",
                ["USER_JWT_PRIVATE_KEY"] = DevPrivateKeyPem,
                ["USER_JWT_KID"] = "env-kid",
                ["REDIS_URL"] = "localhost:6379",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<JwtSigningOptions>>().Value;

        options.PrivateKey.Should().Be(DevPrivateKeyPem);
        options.Kid.Should().Be("env-kid");
    }

    // -------------------------------------------------------------------------
    // Error-cases
    // -------------------------------------------------------------------------

    [Fact]
    public void IssueToken_Throws_WhenUserIsNull()
    {
        var service = CreateService();

        var act = () => service.IssueToken(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RsaAccessTokenService_Throws_WhenPrivateKeyIsEmpty()
    {
        var opts = Options.Create(new JwtSigningOptions { PrivateKey = string.Empty, Kid = "k" });

        var act = () => new RsaAccessTokenService(opts, MakeFrozenClock());

        act.Should().Throw<ArgumentException>();
    }
}
