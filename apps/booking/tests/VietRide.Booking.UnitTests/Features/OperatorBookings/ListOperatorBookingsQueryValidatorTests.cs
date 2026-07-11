using FluentAssertions;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;

namespace VietRide.Booking.UnitTests.Features.OperatorBookings;

public sealed class ListOperatorBookingsQueryValidatorTests
{
    private readonly ListOperatorBookingsQueryValidator _sut = new();

    [Theory]
    [InlineData("createdAt")]
    [InlineData("departureAt")]
    [InlineData("bookingCode")]
    [InlineData("status")]
    [InlineData("totalAmount")]
    public async Task Validate_AcceptsEveryFrozenSortField(string sortBy)
    {
        var result = await _sut.ValidateAsync(Valid(sortBy: sortBy));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_DoesNotMapInvalidSortThroughValidationAsHttp422()
    {
        var result = await _sut.ValidateAsync(Valid(sortBy: "passengerPhone"));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("CONFIRMED")]
    [InlineData("CONFIRMED,CANCELLED")]
    public async Task Validate_AcceptsSingleAndCsvStatus(string status)
    {
        var result = await _sut.ValidateAsync(Valid(status: status));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    public async Task Validate_RejectsInvalidPaging(int page, int pageSize)
    {
        var result = await _sut.ValidateAsync(Valid(page: page, pageSize: pageSize));
        result.Errors.Should().Contain(e => e.ErrorCode == "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Validate_AcceptsPageSizeAboveCapForHandlerClamping()
    {
        var result = await _sut.ValidateAsync(Valid(pageSize: 101));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("999")]
    [InlineData("CONFIRMED,")]
    [InlineData(",CONFIRMED")]
    [InlineData("CONFIRMED,,CANCELLED")]
    [InlineData("UNDEFINED")]
    public async Task Validate_RejectsNumericUndefinedAndEmptyCsvStatusTokens(string status)
    {
        var result = await _sut.ValidateAsync(Valid(status: status));
        result.Errors.Should().Contain(e => e.ErrorCode == "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("1234567890123456789012345678901")]
    public async Task Validate_RejectsBlankOrOver30CharacterBookingCode(string bookingCode)
    {
        var result = await _sut.ValidateAsync(Valid(bookingCode: bookingCode));
        result.Errors.Should().Contain(e => e.ErrorCode == "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("0901 234567")]
    [InlineData("0901-234567")]
    [InlineData("(0901)234567")]
    [InlineData("0901' OR 1=1 --")]
    public async Task Validate_RejectsInternalPhoneSeparators(string phone)
    {
        var result = await _sut.ValidateAsync(Valid(phone: phone));
        result.Errors.Should().Contain(e => e.ErrorCode == "VALIDATION_ERROR");
    }

    private static ListOperatorBookingsQuery Valid(
        string? sortBy = null,
        string? status = null,
        string? phone = null,
        string? bookingCode = null,
        int page = 1,
        int pageSize = 20)
        => new(Guid.NewGuid(), status, null, null, phone, bookingCode, page, pageSize, sortBy, "desc");
}
