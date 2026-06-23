namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>
/// Request body for POST /v1/operator/voucher-consents/{id}/reject.
/// </summary>
public sealed class RejectVoucherConsentRequest
{
    /// <summary>
    /// Optional reason for rejection. When an ACCEPTED consent is revoked, this documents
    /// why the operator opted out after previously accepting.
    /// </summary>
    public string? Reason { get; init; }
}
