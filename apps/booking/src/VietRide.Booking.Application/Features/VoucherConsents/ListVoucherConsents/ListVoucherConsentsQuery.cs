using MediatR;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;

/// <summary>
/// Query for GET /v1/operator/voucher-consents — returns operator-scoped consent rows,
/// optionally filtered by status. Tenant isolation: only consents for <see cref="CallerOperatorId"/>.
/// <para>
/// <see cref="Status"/> is a raw string (e.g. "PENDING"); the handler parses it to
/// <see cref="OperatorVoucherConsentStatus"/> so the Api layer stays domain-free.
/// </para>
/// </summary>
public sealed record ListVoucherConsentsQuery(
    Guid CallerOperatorId,
    /// <summary>Optional raw status string (PENDING | ACCEPTED | REJECTED). Null = no filter.</summary>
    string? Status) : IRequest<ListVoucherConsentsResult>;
