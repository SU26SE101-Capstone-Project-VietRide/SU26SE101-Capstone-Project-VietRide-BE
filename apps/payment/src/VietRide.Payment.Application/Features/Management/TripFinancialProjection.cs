namespace VietRide.Payment.Application.Features.Management;

public sealed record TripFinancialProjection(
    Guid OperatorId,
    Guid TripId,
    long GrossSalesAmount,
    long PassengerPaidAmount,
    long VietRideFundedAmount,
    long OperatorFundedDiscountAmount,
    long RefundAmount,
    long RecognizedAdjustmentAmount,
    long NetEntitlementAmount,
    bool MetadataComplete);
