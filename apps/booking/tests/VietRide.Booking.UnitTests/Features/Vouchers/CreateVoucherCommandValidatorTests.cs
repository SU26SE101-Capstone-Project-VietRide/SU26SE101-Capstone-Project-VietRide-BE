using FluentAssertions;
using VietRide.Booking.Application.Features.Vouchers.CreateVoucher;

namespace VietRide.Booking.UnitTests.Features.Vouchers;

/// <summary>
/// Unit tests for <see cref="CreateVoucherCommandValidator"/>.
/// </summary>
public class CreateVoucherCommandValidatorTests
{
    private static readonly Guid AdminUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OperatorId1 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly CreateVoucherCommandValidator _validator = new();

    // -----------------------------------------------------------------------
    // Happy path — valid OPERATOR_FUNDED with non-empty applicableOperatorIds
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_OperatorFunded_WithApplicableOperatorIds_IsValid()
    {
        var command = BuildOperatorFundedCommand(applicableOperatorIds: [OperatorId1]);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Q3 acceptance: OPERATOR_FUNDED + null applicableOperatorIds → VALIDATION_ERROR
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_OperatorFunded_NullApplicableOperatorIds_FailsWithValidationError()
    {
        // Arrange — Q3 RESOLVED: null list is invalid for OPERATOR_FUNDED
        var command = BuildOperatorFundedCommand(applicableOperatorIds: null);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorCode == "VALIDATION_ERROR" &&
            e.PropertyName == nameof(CreateVoucherCommand.ApplicableOperatorIds));
    }

    // -----------------------------------------------------------------------
    // VIETRIDE_FUNDED — null applicableOperatorIds is allowed (no Q3 rule fires)
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_VietrideFunded_NullApplicableOperatorIds_IsValid()
    {
        var command = BuildVietrideFundedCommand(applicableOperatorIds: null);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CreateVoucherCommand BuildOperatorFundedCommand(
        IReadOnlyList<Guid>? applicableOperatorIds) =>
        new(
            Code: "OPFUND01",
            Name: "Operator Special",
            Type: "FIXED_AMOUNT",
            Value: 20_000,
            MinOrderAmount: 50_000,
            MaxDiscountAmount: null,
            TotalUsageLimit: 50,
            PerUserLimit: null,
            ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
            ValidUntil: DateTimeOffset.UtcNow.AddDays(15),
            ApplicableOperatorIds: applicableOperatorIds,
            ApplicableRouteIds: null,
            FundingType: "OPERATOR_FUNDED",
            CreatedByUserId: AdminUserId);

    private static CreateVoucherCommand BuildVietrideFundedCommand(
        IReadOnlyList<Guid>? applicableOperatorIds) =>
        new(
            Code: "PROMO2024",
            Name: "Summer Sale",
            Type: "PERCENT_OFF",
            Value: 10,
            MinOrderAmount: 100_000,
            MaxDiscountAmount: 50_000,
            TotalUsageLimit: 100,
            PerUserLimit: 1,
            ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
            ValidUntil: DateTimeOffset.UtcNow.AddDays(30),
            ApplicableOperatorIds: applicableOperatorIds,
            ApplicableRouteIds: null,
            FundingType: "VIETRIDE_FUNDED",
            CreatedByUserId: AdminUserId);
}
