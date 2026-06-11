using FluentAssertions;
using VietRide.Booking.Domain.ValueObjects;

namespace VietRide.Booking.UnitTests.Domain;

public class BookingCodeTests
{
    [Fact]
    public void Generate_ProducesCorrectFormat()
    {
        var now = new DateTimeOffset(2026, 5, 18, 0, 0, 0, TimeSpan.Zero);

        var code = BookingCode.Generate(now);

        code.Value.Should().StartWith("VR-20260518-");
        code.Value.Should().HaveLength(20); // VR- (3) + yyyyMMdd (8) + - (1) + 8chars = 20
        code.Value[11].Should().Be('-');
        code.Value[12..].Should().MatchRegex("^[0-9A-HJKMNP-TV-Z]{8}$");
    }

    [Fact]
    public void Generate_ProducesUniqueCodesForSameTime()
    {
        var now = DateTimeOffset.UtcNow;

        var a = BookingCode.Generate(now);
        var b = BookingCode.Generate(now);

        // Statistically they should differ; extremely unlikely to collide in 8-char base32
        a.Should().NotBe(b);
    }

    [Fact]
    public void Parse_ValidCode_ReturnsCode()
    {
        const string raw = "VR-20260518-ABCD1234";

        var code = BookingCode.Parse(raw);

        code.Value.Should().Be(raw);
    }

    [Fact]
    public void Parse_NullOrWhitespace_Throws()
    {
        var act = () => BookingCode.Parse(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_WrongFormat_Throws()
    {
        var act = () => BookingCode.Parse("BADFORMAT");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_MissingSeparatorAtPosition11_Throws()
    {
        // Length 20, starts with VR-, but position 11 is 'X' instead of '-'
        // VR-20260518XABCD1234 — position 11 = 'X'
        const string raw = "VR-20260518XABCD1234";

        var act = () => BookingCode.Parse(raw);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*VR-yyyyMMdd-XXXXXXXX*");
    }

    [Fact]
    public void Parse_InvalidDatePart_Throws()
    {
        // Length 20, starts with VR-, position 11 is '-', but date part is invalid
        // VR-99999999-ABCD1234 — "99999999" is not a valid yyyyMMdd
        const string raw = "VR-99999999-ABCD1234";

        var act = () => BookingCode.Parse(raw);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*invalid date part*");
    }

    [Fact]
    public void Parse_DatePartWithInvalidDay_Throws()
    {
        // VR-20261340-ABCD1234 — month 13, day 40 is not a valid date
        const string raw = "VR-20261340-ABCD1234";

        var act = () => BookingCode.Parse(raw);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*invalid date part*");
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var code = BookingCode.Parse("VR-20260518-ABCD1234");

        code.ToString().Should().Be("VR-20260518-ABCD1234");
    }
}
