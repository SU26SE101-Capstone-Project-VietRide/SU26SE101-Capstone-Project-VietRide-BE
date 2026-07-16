using FluentAssertions;
using VietRide.Trip.Application.Features.Trips.EditTrip;

namespace VietRide.Trip.UnitTests.Features.Trips.EditTrip;

public sealed class EditTripValidatorTests
{
    private readonly EditTripValidator validator = new();

    [Fact]
    public async Task EmptyRecognizedBody_IsInvalid()
    {
        var result = await validator.ValidateAsync(CreateCommand());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorCode == "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("baseFare")]
    [InlineData("vehicleId")]
    [InlineData("routeId")]
    public async Task ExplicitNullForNonNullableField_IsInvalid(string field)
    {
        var command = CreateCommand(
            baseFareSpecified: field == "baseFare",
            vehicleIdSpecified: field == "vehicleId",
            routeIdSpecified: field == "routeId");

        var result = await validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorCode == "VALIDATION_ERROR");
    }

    [Fact]
    public async Task ExplicitNullNotes_IsValidClear()
    {
        var result = await validator.ValidateAsync(CreateCommand(notesSpecified: true));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task NotesOverTwoThousandCharacters_IsInvalid()
    {
        var result = await validator.ValidateAsync(CreateCommand(notesSpecified: true, notes: new string('x', 2001)));

        result.IsValid.Should().BeFalse();
    }

    private static EditTripCommand CreateCommand(
        bool baseFareSpecified = false,
        long? baseFare = null,
        bool notesSpecified = false,
        string? notes = null,
        bool vehicleIdSpecified = false,
        Guid? vehicleId = null,
        bool routeIdSpecified = false,
        Guid? routeId = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "request-1",
            baseFareSpecified,
            baseFare,
            notesSpecified,
            notes,
            vehicleIdSpecified,
            vehicleId,
            routeIdSpecified,
            routeId);
}
