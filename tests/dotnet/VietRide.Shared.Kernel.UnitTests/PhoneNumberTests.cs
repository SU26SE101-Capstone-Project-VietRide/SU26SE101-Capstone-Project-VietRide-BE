using FluentAssertions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;

namespace VietRide.Shared.Kernel.UnitTests;

public class PhoneNumberTests
{
    // -------------------------------------------------------------------------
    // Parse — valid E.164 VN (9 or 10 digits after +84)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("+84901234567")]   // 9 digits
    [InlineData("+84987654321")]   // 9 digits
    [InlineData("+84123456789")]   // 9 digits
    [InlineData("+849012345678")]  // 10 digits — widened per Task 3.4 / Q3
    public void Parse_Valid_E164_Vn_Phone_Succeeds(string input)
    {
        var phone = PhoneNumber.Parse(input);
        phone.Value.Should().Be(input);
    }

    // -------------------------------------------------------------------------
    // Parse — invalid formats must throw
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("0901234567")]       // local format — use Normalize, not Parse
    [InlineData("+8512345678")]      // wrong country code
    [InlineData("+8490123")]         // too short
    [InlineData("+8490123456789")]   // 11 digits after +84 — too long
    [InlineData("")]
    [InlineData("not-a-phone")]
    public void Parse_Invalid_Throws_ArgumentException(string input)
    {
        var act = () => PhoneNumber.Parse(input);
        act.Should().Throw<ArgumentException>();
    }

    // -------------------------------------------------------------------------
    // TryParse
    // -------------------------------------------------------------------------

    [Fact]
    public void TryParse_Valid_Returns_True()
    {
        PhoneNumber.TryParse("+84901234567", out var phone).Should().BeTrue();
        phone.Value.Should().Be("+84901234567");
    }

    [Fact]
    public void TryParse_Invalid_Returns_False()
    {
        PhoneNumber.TryParse("invalid", out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_Trims_Whitespace()
    {
        PhoneNumber.Parse("  +84901234567  ").Value.Should().Be("+84901234567");
    }

    // -------------------------------------------------------------------------
    // Normalize — local → E.164
    // -------------------------------------------------------------------------

    [Fact]
    public void Normalize_LocalNineDigit_ReturnsE164()
    {
        var phone = PhoneNumber.Normalize("0901234567");
        phone.Value.Should().Be("+84901234567");
    }

    [Fact]
    public void Normalize_LocalTenDigit_ReturnsE164()
    {
        var phone = PhoneNumber.Normalize("09012345678");
        phone.Value.Should().NotBeNullOrWhiteSpace();
        phone.Value.Should().StartWith("+84");
    }

    [Fact]
    public void Normalize_AlreadyE164_IsIdempotent()
    {
        var phone = PhoneNumber.Normalize("+84901234567");
        phone.Value.Should().Be("+84901234567");
    }

    [Fact]
    public void Normalize_Null_Throws_ArgumentException()
    {
        var act = () => PhoneNumber.Normalize(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Normalize_Empty_Throws_ArgumentException()
    {
        var act = () => PhoneNumber.Normalize(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Normalize_Garbage_Throws_ArgumentException()
    {
        var act = () => PhoneNumber.Normalize("garbage");
        act.Should().Throw<ArgumentException>();
    }
}
