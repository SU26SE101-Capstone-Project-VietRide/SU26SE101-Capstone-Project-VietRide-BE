using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class DevBookingServiceClient : IBookingServiceClient
{
    private readonly ILogger<DevBookingServiceClient> _logger;

    public DevBookingServiceClient(ILogger<DevBookingServiceClient> logger)
    {
        _logger = logger;
    }

    public Task<BookingHistoryOutcome> GetPassengerHistoryAsync(
        Guid userId,
        string? status,
        string? from,
        string? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new BookingHistoryOutcome(
            true,
            new BookingHistoryPage([], page, pageSize, 0, 0, false, page > 1),
            null));

    public Task<BookingLookupOutcome> GetBookingSnapshotAsync(
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Booking stub for GetBookingSnapshotAsync({BookingId}).", bookingId);

        var snapshot = new BookingSnapshot(
            BookingId: bookingId,
            UserId: Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            TripId: Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
            Status: "CONFIRMED",
            ActiveTicketCount: 1);

        return Task.FromResult(new BookingLookupOutcome(BookingLookupOutcomeKind.Success, snapshot, null));
    }

    public Task<VoucherValidationOutcome> ValidateVoucherAsync(
        string voucherCode,
        Guid operatorId,
        Guid routeId,
        Guid userId,
        long orderAmount,
        string paymentMethod,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new VoucherValidationOutcome(
            VoucherValidationOutcomeKind.Success,
            Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"),
            Math.Min(10_000, orderAmount),
            null));

    public Task<VoucherUsageOutcome> RecordVoucherUsageAsync(
        Guid voucherId,
        Guid userId,
        Guid parcelId,
        long discountAmount,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new VoucherUsageOutcome(VoucherUsageOutcomeKind.Success, Guid.NewGuid(), null));

    public Task DeleteVoucherUsageByReferenceAsync(Guid parcelId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<AvailableVoucherDto>> GetAvailableParcelVouchersAsync(
        Guid userId,
        Guid operatorId,
        Guid routeId,
        string? paymentMethod,
        long? orderAmount,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AvailableVoucherDto>>([]);
}
