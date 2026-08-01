using FluentAssertions;
using VietRide.Payment.Application.Features.Internal.Payments.LookupRedirectSessions;

namespace VietRide.Payment.UnitTests.Features.Internal.Payments.LookupRedirectSessions;

public sealed class LookupRedirectSessionsQueryValidatorTests
{
    private readonly LookupRedirectSessionsQueryValidator _validator = new();

    [Fact]
    public async Task Validate_WhenRequestIsValid_AcceptsAllExactReferenceTypes()
    {
        var query = new LookupRedirectSessionsQuery(
            Guid.NewGuid(),
            [
                new("BOOKING", Guid.NewGuid()),
                new("BOOKING_GROUP", Guid.NewGuid()),
                new("PARCEL", Guid.NewGuid()),
                new("PARCEL_ADDITIONAL", Guid.NewGuid()),
            ]);

        var result = await _validator.ValidateAsync(query);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_WhenReferenceCountIsExactlyMaximum_AcceptsRequest()
    {
        var references = Enumerable.Range(0, 100)
            .Select(_ => new LookupRedirectSessionsQuery.Reference("BOOKING", Guid.NewGuid()))
            .ToArray();

        var result = await _validator.ValidateAsync(
            new LookupRedirectSessionsQuery(Guid.NewGuid(), references));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_WhenUserIdIsEmpty_RejectsRequest()
    {
        var query = ValidQuery() with { UserId = Guid.Empty };

        var result = await _validator.ValidateAsync(query);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Validate_WhenReferenceCountIsOutsideRange_RejectsRequest(int count)
    {
        var references = Enumerable.Range(0, count)
            .Select(_ => new LookupRedirectSessionsQuery.Reference("BOOKING", Guid.NewGuid()))
            .ToArray();
        var query = new LookupRedirectSessionsQuery(Guid.NewGuid(), references);

        var result = await _validator.ValidateAsync(query);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_WhenCompositeReferenceIsDuplicated_RejectsRequest()
    {
        var reference = new LookupRedirectSessionsQuery.Reference("BOOKING", Guid.NewGuid());
        var query = new LookupRedirectSessionsQuery(Guid.NewGuid(), [reference, reference]);

        var result = await _validator.ValidateAsync(query);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("booking")]
    [InlineData("Booking")]
    [InlineData("TOP_UP")]
    [InlineData("")]
    public async Task Validate_WhenReferenceTypeIsNotAnExactAllowedValue_RejectsRequest(string referenceType)
    {
        var query = new LookupRedirectSessionsQuery(
            Guid.NewGuid(),
            [new(referenceType, Guid.NewGuid())]);

        var result = await _validator.ValidateAsync(query);

        result.IsValid.Should().BeFalse();
    }

    private static LookupRedirectSessionsQuery ValidQuery()
        => new(Guid.NewGuid(), [new("BOOKING", Guid.NewGuid())]);
}
