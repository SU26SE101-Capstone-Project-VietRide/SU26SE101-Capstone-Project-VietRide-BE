namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface IBookingServiceClient
{
    Task<BookingHistoryOutcome> GetPassengerHistoryAsync(
        Guid userId,
        string? status,
        string? from,
        string? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<BookingLookupOutcome> GetBookingSnapshotAsync(
        Guid bookingId,
        CancellationToken cancellationToken = default);

    Task<VoucherValidationOutcome> ValidateVoucherAsync(
        string voucherCode,
        Guid operatorId,
        Guid routeId,
        Guid userId,
        long orderAmount,
        string paymentMethod,
        CancellationToken cancellationToken = default);

    Task<VoucherUsageOutcome> RecordVoucherUsageAsync(
        Guid voucherId,
        Guid userId,
        Guid parcelId,
        long discountAmount,
        CancellationToken cancellationToken = default);

    Task DeleteVoucherUsageByReferenceAsync(
        Guid parcelId,
        Guid voucherUsageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvailableVoucherDto>> GetAvailableParcelVouchersAsync(
        Guid userId,
        Guid operatorId,
        Guid routeId,
        string? paymentMethod,
        long? orderAmount,
        CancellationToken cancellationToken = default);
}
