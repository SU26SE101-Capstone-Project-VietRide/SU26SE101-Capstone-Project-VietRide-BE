using FluentAssertions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;

namespace VietRide.Shared.Kernel.UnitTests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+84901234567")]
    [InlineData("+84987654321")]
    [InlineData("+84123456789")]
    public void Parse_Valid_E164_Vn_Phone_Succeeds(string input)
    {
        var phone = PhoneNumber.Parse(input);
        phone.Value.Should().Be(input);
    }

    [Theory]
    [InlineData("0901234567")]      // missing +84
    [InlineData("+8512345678")]     // wrong country code
    [InlineData("+8490123")]        // too short
    [InlineData("+849012345678")]   // too long
    [InlineData("")]
    [InlineData("not-a-phone")]
    public void Parse_Invalid_Throws_ArgumentException(string input)
    {
        var act = () => PhoneNumber.Parse(input);
        act.Should().Throw<ArgumentException>();
    }

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
}
