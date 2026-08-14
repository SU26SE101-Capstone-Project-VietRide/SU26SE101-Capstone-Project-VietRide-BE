using MediatR;
using VietRide.Payment.Application.Models;
using VietRide.Shared.Application.Behaviors;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Application.Features.Management;

public sealed record PageOptions(
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string SortDir = "desc",
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

public sealed record OperatorWalletDto(
    Guid OperatorId,
    long Balance,
    long PendingHoldAmount,
    long EligibleAmount,
    DateTimeOffset UpdatedAt,
    string Currency = "VND",
    long AwaitingTripCompletionAmount = 0,
    int AwaitingTripCompletionCount = 0,
    int PendingHoldCount = 0,
    int EligibleCount = 0,
    DateTimeOffset? NextEligibleAt = null,
    DateTimeOffset? NextScheduledSettlementAttemptAt = null,
    long LifetimeSettledAmount = 0,
    LastSettlementDto? LastSettlement = null,
    bool WithdrawalSupported = false,
    DateTimeOffset CalculatedAt = default);

public sealed record LastSettlementDto(
    Guid SettlementId,
    long Amount,
    string Method,
    DateTimeOffset SettledAt);

public sealed record WalletTransactionDto(
    Guid TransactionId,
    string Type,
    long Amount,
    long BalanceBefore,
    long BalanceAfter,
    string ReferenceType,
    Guid? ReferenceId,
    string? Note,
    DateTimeOffset CreatedAt,
    long SignedAmount = 0,
    string Currency = "VND",
    RelatedSettlementDto? RelatedSettlement = null,
    string ActorType = "SYSTEM",
    FinancialActorDto? Actor = null,
    string? AdjustmentReason = null,
    string DataCompleteness = "COMPLETE",
    IReadOnlyList<string>? MissingFields = null);

public sealed record RelatedSettlementDto(
    Guid SettlementId,
    Guid TripId,
    string Method);

public sealed record SettlementDto(
    Guid SettlementId,
    Guid TripId,
    string Status,
    DateTimeOffset EligibleAt,
    long NetAmount,
    string? SettlementMethod,
    DateTimeOffset? SettledAt,
    DateTimeOffset CreatedAt,
    FinancialActorDto? SettledBy = null,
    DateTimeOffset TripTerminalAt = default,
    Guid? WalletTransactionId = null,
    SettlementFinancialBreakdownDto? FinancialBreakdown = null,
    string ProcessingState = "ON_HOLD",
    DateTimeOffset? NextScheduledSettlementAttemptAt = null,
    string? DelayReason = null,
    int AttemptCount = 0,
    DateTimeOffset? LastAttemptAt = null,
    DateTimeOffset? NextRetryAt = null,
    string? CancelReason = null,
    SettlementTripDto? Trip = null,
    string DataCompleteness = "COMPLETE");

public sealed record SettlementFinancialBreakdownDto(
    long GrossSalesAmount,
    long PassengerPaidAmount,
    long VietRideFundedAmount,
    long OperatorFundedDiscountAmount,
    long RefundAmount,
    long RecognizedAdjustmentAmount,
    long NetEntitlementAmount);

public sealed record SettlementTripDto(
    DateTimeOffset DepartureAt,
    Guid RouteId,
    string RouteName,
    string OriginName,
    string DestinationName);

public sealed record AdminSettlementDto(
    Guid SettlementId,
    Guid TripId,
    Guid OperatorId,
    string Status,
    DateTimeOffset EligibleAt,
    long NetAmount,
    string? SettlementMethod,
    DateTimeOffset? SettledAt,
    DateTimeOffset CreatedAt,
    int FailureCount,
    string? ActiveFailureCode,
    string? Severity,
    FinancialOperatorDto? Operator,
    FinancialActorDto? SettledBy);

public sealed record FinancialOperatorDto(
    Guid OperatorId,
    string Name,
    string? LogoUrl,
    string? ContactPhone);

public sealed record FinancialActorDto(
    Guid UserId,
    string DisplayName,
    string Email,
    string Role);

public sealed record PlatformWalletTransactionDto(
    Guid TransactionId,
    string Type,
    long Amount,
    long BalanceBefore,
    long BalanceAfter,
    string ReferenceType,
    Guid? ReferenceId,
    string? Note,
    DateTimeOffset CreatedAt,
    string ActorType,
    FinancialActorDto? Actor);

public sealed record LedgerEntryDto(
    Guid LedgerEntryId,
    Guid? TripId,
    string EntryType,
    long Amount,
    string ReferenceType,
    Guid ReferenceId,
    DateTimeOffset CreatedAt,
    string? Note = null,
    string ActorType = "SYSTEM",
    FinancialActorDto? Actor = null,
    string? ReferenceCode = null,
    DateTimeOffset OccurredAt = default,
    string OccurredAtSource = "LEDGER_CREATED_AT_FALLBACK",
    long? OperatorFundedVoucherAmount = null,
    string? AdjustmentReason = null,
    bool AffectsRevenue = false,
    bool AffectsSettlement = false,
    LedgerSettlementDto? Settlement = null,
    string DataCompleteness = "COMPLETE",
    IReadOnlyList<string>? MissingFields = null);

public sealed record LedgerSettlementDto(
    Guid SettlementId,
    string Status,
    DateTimeOffset EligibleAt,
    DateTimeOffset? SettledAt,
    Guid? WalletTransactionId);

public sealed record InvoiceListItemDto(
    Guid InvoiceId,
    string InvoiceNumber,
    Guid PaymentId,
    string Status,
    long Amount,
    string BillingPeriod,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    string PdfGenerationStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? IssuedAt);

public sealed record InvoiceDetailDto(
    Guid InvoiceId,
    string InvoiceNumber,
    Guid PaymentId,
    string Status,
    long Amount,
    string BillingPeriod,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    string PdfGenerationStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? IssuedAt,
    string PlanName,
    SubscriptionBuyerSnapshotV1 BuyerSnapshot,
    string InvoiceWebUrl,
    string DownloadApiUrl);

public sealed record PlatformWalletDto(Guid PlatformWalletId, long Balance, DateTimeOffset UpdatedAt);

public sealed record AdjustmentRequest(string Type, long Amount, string Note);

public sealed record AdjustmentResult(
    Guid TransactionId,
    string Type,
    long Amount,
    long BalanceBefore,
    long BalanceAfter,
    string ReferenceType,
    Guid? ReferenceId,
    string Note,
    DateTimeOffset CreatedAt);

public sealed record ManualSettlementResult(
    Guid SettlementId,
    Guid TripId,
    Guid OperatorId,
    long NetAmount,
    string Status,
    string SettlementMethod,
    DateTimeOffset? SettledAt);

public interface IFinancialManagementService
{
    Task<OperatorWalletDto> GetOperatorWalletAsync(Guid operatorId, CancellationToken cancellationToken);
    Task<PagedResult<WalletTransactionDto>> ListOperatorTransactionsAsync(Guid operatorId, PageOptions options, string? type, string? referenceType, CancellationToken cancellationToken, string? search = null, string? dateField = null);
    Task<PagedResult<SettlementDto>> ListOperatorSettlementsAsync(Guid operatorId, PageOptions options, string? status, Guid? tripId, CancellationToken cancellationToken, string? search = null, string? dateField = null);
    Task<PagedResult<LedgerEntryDto>> ListOperatorLedgerAsync(Guid operatorId, PageOptions options, Guid? tripId, string? entryType, string? referenceType, CancellationToken cancellationToken, string? search = null, string? dateField = null);
    Task<PagedResult<InvoiceListItemDto>> ListInvoicesAsync(Guid operatorId, PageOptions options, string? status, CancellationToken cancellationToken);
    Task<PagedResult<InvoiceListItemDto>> ListInvoicesFilteredAsync(
        Guid operatorId,
        PageOptions options,
        string? status,
        string? search,
        CancellationToken cancellationToken)
        => ListInvoicesAsync(operatorId, options, status, cancellationToken);
    Task<InvoiceDetailDto> GetInvoiceAsync(Guid operatorId, Guid invoiceId, CancellationToken cancellationToken);
    Task<PagedResult<AdminSettlementDto>> ListAdminSettlementsAsync(PageOptions options, Guid? operatorId, string? status, Guid? tripId, bool stuckOnly, string? severity, CancellationToken cancellationToken, string? search = null);
    Task<PlatformWalletDto> GetPlatformWalletAsync(CancellationToken cancellationToken);
    Task<PagedResult<PlatformWalletTransactionDto>> ListPlatformTransactionsAsync(PageOptions options, string? type, string? referenceType, CancellationToken cancellationToken, string? search = null);
    Task<AdjustmentResult> AdjustPlatformWalletAsync(AdjustmentRequest request, Guid actorUserId, CancellationToken cancellationToken);
    Task<AdjustmentResult> AdjustOperatorWalletAsync(Guid operatorId, AdjustmentRequest request, Guid actorUserId, CancellationToken cancellationToken);
    Task<ManualSettlementResult> SettleAsync(Guid settlementId, Guid actorUserId, CancellationToken cancellationToken);
}

public sealed record GetOperatorWalletQuery(Guid OperatorId) : IRequest<OperatorWalletDto>;
public sealed record ListOperatorTransactionsQuery(Guid OperatorId, PageOptions Options, string? Type, string? ReferenceType, string? Search = null, string? DateField = null) : IRequest<PagedResult<WalletTransactionDto>>;
public sealed record ListOperatorSettlementsQuery(Guid OperatorId, PageOptions Options, string? Status, Guid? TripId, string? Search = null, string? DateField = null) : IRequest<PagedResult<SettlementDto>>;
public sealed record ListOperatorLedgerQuery(Guid OperatorId, PageOptions Options, Guid? TripId, string? EntryType, string? ReferenceType, string? Search = null, string? DateField = null) : IRequest<PagedResult<LedgerEntryDto>>;
public sealed record ListOperatorInvoicesQuery(Guid OperatorId, PageOptions Options, string? Status, string? Search = null) : IRequest<PagedResult<InvoiceListItemDto>>;
public sealed record GetOperatorInvoiceQuery(Guid OperatorId, Guid InvoiceId) : IRequest<InvoiceDetailDto>;
public sealed record ListAdminSettlementsQuery(PageOptions Options, Guid? OperatorId, string? Status, Guid? TripId, bool StuckOnly, string? Severity, string? Search = null) : IRequest<PagedResult<AdminSettlementDto>>;
public sealed record GetPlatformWalletQuery : IRequest<PlatformWalletDto>;
public sealed record ListPlatformTransactionsQuery(PageOptions Options, string? Type, string? ReferenceType, string? Search = null) : IRequest<PagedResult<PlatformWalletTransactionDto>>;
[SkipTransaction]
public sealed record AdjustPlatformWalletCommand(AdjustmentRequest Request, Guid ActorUserId) : IRequest<AdjustmentResult>;

[SkipTransaction]
public sealed record AdjustOperatorWalletCommand(Guid OperatorId, AdjustmentRequest Request, Guid ActorUserId) : IRequest<AdjustmentResult>;

[SkipTransaction]
public sealed record SettleTripManuallyCommand(Guid SettlementId, Guid ActorUserId) : IRequest<ManualSettlementResult>;

public sealed class FinancialManagementHandlers :
    IRequestHandler<GetOperatorWalletQuery, OperatorWalletDto>,
    IRequestHandler<ListOperatorTransactionsQuery, PagedResult<WalletTransactionDto>>,
    IRequestHandler<ListOperatorSettlementsQuery, PagedResult<SettlementDto>>,
    IRequestHandler<ListOperatorLedgerQuery, PagedResult<LedgerEntryDto>>,
    IRequestHandler<ListOperatorInvoicesQuery, PagedResult<InvoiceListItemDto>>,
    IRequestHandler<GetOperatorInvoiceQuery, InvoiceDetailDto>,
    IRequestHandler<ListAdminSettlementsQuery, PagedResult<AdminSettlementDto>>,
    IRequestHandler<GetPlatformWalletQuery, PlatformWalletDto>,
    IRequestHandler<ListPlatformTransactionsQuery, PagedResult<PlatformWalletTransactionDto>>,
    IRequestHandler<AdjustPlatformWalletCommand, AdjustmentResult>,
    IRequestHandler<AdjustOperatorWalletCommand, AdjustmentResult>,
    IRequestHandler<SettleTripManuallyCommand, ManualSettlementResult>
{
    private readonly IFinancialManagementService _service;

    public FinancialManagementHandlers(IFinancialManagementService service) => _service = service;

    public Task<OperatorWalletDto> Handle(GetOperatorWalletQuery request, CancellationToken ct) => _service.GetOperatorWalletAsync(request.OperatorId, ct);
    public Task<PagedResult<WalletTransactionDto>> Handle(ListOperatorTransactionsQuery request, CancellationToken ct) => _service.ListOperatorTransactionsAsync(request.OperatorId, request.Options, request.Type, request.ReferenceType, ct, request.Search, request.DateField);
    public Task<PagedResult<SettlementDto>> Handle(ListOperatorSettlementsQuery request, CancellationToken ct) => _service.ListOperatorSettlementsAsync(request.OperatorId, request.Options, request.Status, request.TripId, ct, request.Search, request.DateField);
    public Task<PagedResult<LedgerEntryDto>> Handle(ListOperatorLedgerQuery request, CancellationToken ct) => _service.ListOperatorLedgerAsync(request.OperatorId, request.Options, request.TripId, request.EntryType, request.ReferenceType, ct, request.Search, request.DateField);
    public Task<PagedResult<InvoiceListItemDto>> Handle(ListOperatorInvoicesQuery request, CancellationToken ct) => _service.ListInvoicesFilteredAsync(request.OperatorId, request.Options, request.Status, request.Search, ct);
    public Task<InvoiceDetailDto> Handle(GetOperatorInvoiceQuery request, CancellationToken ct) => _service.GetInvoiceAsync(request.OperatorId, request.InvoiceId, ct);
    public Task<PagedResult<AdminSettlementDto>> Handle(ListAdminSettlementsQuery request, CancellationToken ct) => _service.ListAdminSettlementsAsync(request.Options, request.OperatorId, request.Status, request.TripId, request.StuckOnly, request.Severity, ct, request.Search);
    public Task<PlatformWalletDto> Handle(GetPlatformWalletQuery request, CancellationToken ct) => _service.GetPlatformWalletAsync(ct);
    public Task<PagedResult<PlatformWalletTransactionDto>> Handle(ListPlatformTransactionsQuery request, CancellationToken ct) => _service.ListPlatformTransactionsAsync(request.Options, request.Type, request.ReferenceType, ct, request.Search);
    public Task<AdjustmentResult> Handle(AdjustPlatformWalletCommand request, CancellationToken ct) => _service.AdjustPlatformWalletAsync(request.Request, request.ActorUserId, ct);
    public Task<AdjustmentResult> Handle(AdjustOperatorWalletCommand request, CancellationToken ct) => _service.AdjustOperatorWalletAsync(request.OperatorId, request.Request, request.ActorUserId, ct);
    public Task<ManualSettlementResult> Handle(SettleTripManuallyCommand request, CancellationToken ct) => _service.SettleAsync(request.SettlementId, request.ActorUserId, ct);
}
