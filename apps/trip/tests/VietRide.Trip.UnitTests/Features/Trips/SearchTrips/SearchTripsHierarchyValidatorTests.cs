using FluentAssertions;
using VietRide.Trip.Application.Features.Trips.SearchTrips;

namespace VietRide.Trip.UnitTests.Features.Trips.SearchTrips;

public sealed class SearchTripsHierarchyValidatorTests
{
    private readonly SearchTripsValidator validator = new();

    [Fact]
    public void Validate_ProvincePairWithOptionalIndependentWards_IsValid()
    {
        var query = new SearchTripsQuery(
            null,
            null,
            new DateOnly(2026, 8, 20),
            1,
            null,
            "79",
            "26506",
            "01",
            null);

        validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_PartialProvincePair_IsInvalid()
    {
        var query = new SearchTripsQuery(
            null,
            null,
            new DateOnly(2026, 8, 20),
            1,
            null,
            "79",
            null,
            null,
            null);

        validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_PartialStationPair_DoesNotFallThroughToHierarchyMode()
    {
        var query = new SearchTripsQuery(
            Guid.NewGuid(),
            null,
            new DateOnly(2026, 8, 20),
            1,
            null,
            "79",
            null,
            "01",
            null);

        validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_CompleteStationPair_WinsAndIgnoresHierarchyFields()
    {
        var query = new SearchTripsQuery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 8, 20),
            1,
            null,
            "legacy-invalid",
            "legacy-invalid",
            null,
            null);

        validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("HCM")]
    [InlineData("79,01")]
    [InlineData("79-")]
    public void Validate_NonOfficialProvinceCode_IsInvalid(string code)
    {
        var query = new SearchTripsQuery(
            null,
            null,
            new DateOnly(2026, 8, 20),
            1,
            null,
            code,
            null,
            "01",
            null);

        validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NewLocationAliasesWithoutLegacyNames_IsValid()
    {
        var query = new SearchTripsQuery(
            null,
            null,
            new DateOnly(2026, 8, 20),
            1,
            null,
            "79",
            null,
            "01",
            null,
            OriginLocationCode: "26734",
            DestinationLocationCode: "00004");

        validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_LegacyAndNewLocationCodesMatch_IsValid()
    {
        var query = new SearchTripsQuery(
            null,
            null,
            new DateOnly(2026, 8, 20),
            1,
            null,
            "79",
            "26734",
            "01",
            "00004",
            "26734",
            "00004");

        validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_LegacyAndNewLocationCodesDiffer_IsInvalid()
    {
        var query = new SearchTripsQuery(
            null,
            null,
            new DateOnly(2026, 8, 20),
            1,
            null,
            "79",
            "26734",
            "01",
            "00004",
            "26735",
            "00005");

        var result = validator.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.ErrorMessage.Contains("must match", StringComparison.Ordinal));
    }
}
