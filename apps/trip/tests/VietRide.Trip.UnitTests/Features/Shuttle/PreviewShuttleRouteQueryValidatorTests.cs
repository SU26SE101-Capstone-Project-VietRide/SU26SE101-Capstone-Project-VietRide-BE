using FluentAssertions;
using VietRide.Trip.Application.Features.Shuttle;

namespace VietRide.Trip.UnitTests.Features.Shuttle;

public sealed class PreviewShuttleRouteQueryValidatorTests
{
    private readonly PreviewShuttleRouteQueryValidator validator = new();

    [Theory]
    [InlineData("INBOUND_TO_STATION")]
    [InlineData("OUTBOUND_FROM_STATION")]
    public async Task Validate_WhenRequestIsValid_Passes(string direction)
    {
        var result = await validator.ValidateAsync(new PreviewShuttleRouteQuery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            direction,
            DateTimeOffset.UtcNow,
            [Guid.NewGuid(), Guid.NewGuid()]));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_WhenRequestIsInvalid_FailsExpectedFields()
    {
        var duplicatedBookingId = Guid.NewGuid();

        var result = await validator.ValidateAsync(new PreviewShuttleRouteQuery(
            Guid.Empty,
            Guid.Empty,
            "INVALID",
            default,
            [duplicatedBookingId, duplicatedBookingId]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(PreviewShuttleRouteQuery.OperatorId));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(PreviewShuttleRouteQuery.MainTripId));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(PreviewShuttleRouteQuery.Direction));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(PreviewShuttleRouteQuery.ScheduledDepartureTime));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(PreviewShuttleRouteQuery.OrderedBookingIds));
    }

    [Fact]
    public async Task Validate_WhenBookingIdsAreNull_ReturnsValidationFailure()
    {
        var result = await validator.ValidateAsync(new PreviewShuttleRouteQuery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INBOUND_TO_STATION",
            DateTimeOffset.UtcNow,
            null!));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(PreviewShuttleRouteQuery.OrderedBookingIds));
    }
}
