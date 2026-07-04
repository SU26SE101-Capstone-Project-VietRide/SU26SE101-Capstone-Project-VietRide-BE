using System.Text.RegularExpressions;
using FluentAssertions;
using VietRide.Parcel.Domain.Helpers;

namespace VietRide.Parcel.UnitTests.Domain;

public sealed class ParcelCodeGeneratorTests
{
    private static readonly Regex CodePattern = new(
        "^VR-PCL-\\d{8}-[A-HJ-NP-Z2-9]{8}$",
        RegexOptions.Compiled);

    [Fact]
    public void Generate_UsesExpectedFormatAndAlphabet()
    {
        var code = ParcelCodeGenerator.Generate(new DateOnly(2026, 7, 3));

        code.Should().MatchRegex(CodePattern.ToString());
    }

    [Fact]
    public void Generate_UsesProvidedDate()
    {
        var code = ParcelCodeGenerator.Generate(new DateOnly(2026, 7, 3));

        code.Should().StartWith("VR-PCL-20260703-");
    }

    [Fact]
    public void Generate_ProducesDistinctSuffixesAcrossManyCalls()
    {
        var codes = Enumerable.Range(0, 128)
            .Select(_ => ParcelCodeGenerator.Generate(new DateOnly(2026, 7, 3)))
            .ToArray();

        codes.Distinct().Should().HaveCount(codes.Length);
    }
}
