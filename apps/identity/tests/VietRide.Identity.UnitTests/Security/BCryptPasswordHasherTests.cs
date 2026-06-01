using FluentAssertions;
using VietRide.Identity.Infrastructure.Security;

namespace VietRide.Identity.UnitTests.Security;

public sealed class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    // -------------------------------------------------------------------------
    // Happy-path
    // -------------------------------------------------------------------------

    [Fact]
    public void Hash_ProducesNonNullNonEmptyString()
    {
        var hash = _hasher.Hash("secretPassword123");

        hash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Hash_UsesBCryptWorkFactor12()
    {
        var hash = _hasher.Hash("secretPassword123");

        // BCrypt hash format: $2a$<cost>$...
        hash.Should().StartWith("$2a$12$");
    }

    [Fact]
    public void Verify_ReturnsTrue_WhenPasswordMatchesHash()
    {
        const string password = "P@ssw0rd!";
        var hash = _hasher.Hash(password);

        var result = _hasher.Verify(password, hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void Hash_ProducesDifferentHashes_ForSamePassword()
    {
        // BCrypt uses a random salt per call
        const string password = "samePassword";
        var hash1 = _hasher.Hash(password);
        var hash2 = _hasher.Hash(password);

        hash1.Should().NotBe(hash2);
    }

    // -------------------------------------------------------------------------
    // Error-cases
    // -------------------------------------------------------------------------

    [Fact]
    public void Verify_ReturnsFalse_WhenPasswordDoesNotMatch()
    {
        var hash = _hasher.Hash("correctPassword");

        var result = _hasher.Verify("wrongPassword", hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenPlainTextIsEmpty()
    {
        var hash = _hasher.Hash("somePassword");

        var result = _hasher.Verify(string.Empty, hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void Hash_Throws_WhenPlainTextIsEmpty()
    {
        var act = () => _hasher.Hash(string.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
