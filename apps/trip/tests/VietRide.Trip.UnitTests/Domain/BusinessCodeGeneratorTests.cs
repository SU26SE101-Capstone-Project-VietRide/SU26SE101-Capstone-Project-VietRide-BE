using FluentAssertions;
using VietRide.Shared.Kernel.Identifiers;

namespace VietRide.Trip.UnitTests.Domain;

public sealed class BusinessCodeGeneratorTests
{
    [Theory]
    [InlineData("TRIP", 22)]
    [InlineData("STL", 21)]
    [InlineData("OWT", 21)]
    [InlineData("PWT", 21)]
    public void Generate_UsesVietnamBusinessDateAndCrockfordAlphabet(
        string prefix,
        int expectedLength)
    {
        var instant = new DateTimeOffset(2026, 8, 22, 18, 30, 0, TimeSpan.Zero);

        var code = BusinessCodeGenerator.Generate(prefix, instant);

        code.Should().HaveLength(expectedLength);
        code.Should().MatchRegex($"^{prefix}-20260823-[0-9ABCDEFGHJKMNPQRSTVWXYZ]{{8}}$");
    }

    [Theory]
    [InlineData("")]
    [InlineData("trip")]
    [InlineData("TOO-LONG")]
    public void Generate_RejectsInvalidPrefix(string prefix)
    {
        var action = () => BusinessCodeGenerator.Generate(prefix, DateTimeOffset.UtcNow);

        action.Should().Throw<ArgumentException>();
    }
}
