namespace VietRide.Booking.Application.Abstractions.Services;

/// <summary>
/// Generates unique voucher codes.
/// Stateless utility — no repo/EF dependency (lives in Application layer).
/// </summary>
public interface IVoucherCodeGenerator
{
    /// <summary>
    /// Generates an 8-character uppercase base32 code.
    /// Callers are responsible for uniqueness checks (retry on VOUCHER_CODE_CONFLICT).
    /// </summary>
    string Generate();
}
