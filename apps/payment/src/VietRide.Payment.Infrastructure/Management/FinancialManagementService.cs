using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Exceptions;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;
using VietRide.Payment.Application.Features.Management;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Payment.Application.Features.Settlements;
using VietRide.Payment.Application.Features.Settlements.SettleTrip;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Domain.ValueObjects;
using VietRide.Payment.Infrastructure.Invoices;
using VietRide.Payment.Infrastructure.Persistence.Repositories;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.Management;

internal sealed class FinancialManagementService : IFinancialManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PaymentDbContext _db;
    private readonly IOperatorLedgerEntryRepository _ledger;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IIdentityFinancialProjectionClient _identity;
    private readonly ITripRevenueAnalyticsClient _trips;
    private readonly IFinancialActorPrivacyStore _actorPrivacy;
    private readonly TripSettlementService _settlements;
    private readonly OperatorWebOptions _operatorWeb;
    private readonly InvoiceStorageOptions _invoiceStorage;
    private readonly IClock _clock;
    private readonly ILogger<FinancialManagementService> _logger;

    public FinancialManagementService(
        PaymentDbContext db,
        IOperatorLedgerEntryRepository ledger,
        IPlatformWalletRepository platformWallets,
        IIdentityFinancialProjectionClient identity,
        ITripRevenueAnalyticsClient trips,
        IFinancialActorPrivacyStore actorPrivacy,
        TripSettlementService settlements,
        IOptions<OperatorWebOptions> operatorWeb,
        IOptions<InvoiceStorageOptions> invoiceStorage,
        IClock clock,
        ILogger<FinancialManagementService> logger)
    {
        _db = db;
        _ledger = ledger;
        _platformWallets = platformWallets;
        _identity = identity;
        _trips = trips;
        _actorPrivacy = actorPrivacy;
        _settlements = settlements;
        _operatorWeb = operatorWeb.Value;
        _invoiceStorage = invoiceStorage.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<OperatorWalletDto> GetOperatorWalletAsync(Guid operatorId, CancellationToken ct)
    {
        await using var readTransaction = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            : null;
        var wallet = await _db.OperatorWallets.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperatorId == operatorId, ct)
            ?? throw NotFound("OPERATOR_WALLET_NOT_FOUND", "Operator wallet was not found.");
        var projections = await _ledger.GetTripFinancialProjectionsAsync(operatorId, null, ct);
        var settlements = await _db.OperatorTripSettlements.AsNoTracking()
            .Where(item => item.OperatorId == operatorId)
            .ToListAsync(ct);
        var projectionByTrip = projections.ToDictionary(item => item.TripId);
        var settlementTripIds = settlements.Select(item => item.TripId).ToHashSet();
        var awaiting = projections.Where(item => !settlementTripIds.Contains(item.TripId)).ToArray();
        var pendingRows = settlements
            .Where(item => item.Status == OperatorTripSettlementStatus.PENDING_HOLD)
            .ToArray();
        var eligibleRows = settlements
            .Where(item => item.Status == OperatorTripSettlementStatus.ELIGIBLE)
            .ToArray();
        var settledRows = settlements
            .Where(item => item.Status == OperatorTripSettlementStatus.SETTLED)
            .ToArray();
        var calculatedAt = _clock.UtcNow;
        var nextScheduledAttempt = pendingRows
            .Concat(eligibleRows)
            .Select(item => TripSettlementSchedule.GetNextScheduledAttemptAt(
                item.Status,
                item.EligibleAt,
                calculatedAt))
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .DefaultIfEmpty()
            .Min();
        var lastSettlement = settledRows
            .Where(item => item.SettledAt.HasValue)
            .OrderByDescending(item => item.SettledAt)
            .FirstOrDefault();

        var result = new OperatorWalletDto(
            operatorId,
            wallet.Balance.Amount,
            SumCurrentEntitlement(pendingRows, projectionByTrip),
            SumCurrentEntitlement(eligibleRows, projectionByTrip),
            wallet.UpdatedAt,
            Currency: "VND",
            AwaitingTripCompletionAmount: awaiting.Sum(item => item.NetEntitlementAmount),
            AwaitingTripCompletionCount: awaiting.Length,
            PendingHoldCount: pendingRows.Length,
            EligibleCount: eligibleRows.Length,
            NextEligibleAt: pendingRows.Length == 0 ? null : pendingRows.Min(item => item.EligibleAt),
            NextScheduledSettlementAttemptAt: nextScheduledAttempt == default ? null : nextScheduledAttempt,
            LifetimeSettledAmount: settledRows.Sum(item => item.NetAmount),
            LastSettlement: lastSettlement is null
                ? null
                : new LastSettlementDto(
                    lastSettlement.Id,
                    lastSettlement.NetAmount,
                    lastSettlement.SettlementMethod?.ToString() ?? "UNKNOWN",
                    lastSettlement.SettledAt!.Value),
            WithdrawalSupported: false,
            CalculatedAt: calculatedAt);
        if (readTransaction is not null)
            await readTransaction.CommitAsync(ct);
        return result;
    }

    public async Task<PagedResult<WalletTransactionDto>> ListOperatorTransactionsAsync(
        Guid operatorId,
        PageOptions options,
        string? type,
        string? referenceType,
        CancellationToken ct,
        string? search = null,
        string? dateField = null)
    {
        ValidatePage(options, ["createdAt", "amount"]);
        ValidateDateField(dateField, ["createdAt"]);
        var normalizedSearch = NormalizeSearch(search);
        var query = _db.OperatorWalletTransactions.AsNoTracking().Where(item => item.OperatorId == operatorId);
        var settlements = _db.OperatorTripSettlements.AsNoTracking()
            .Where(item => item.OperatorId == operatorId);
        var ledger = _db.OperatorLedgerEntries.AsNoTracking()
            .Where(item => item.OperatorId == operatorId);
        if (ParseOptional<OperatorWalletTransactionType>(type) is { } parsedType)
            query = query.Where(item => item.Type == parsedType);
        if (ParseOptional<OperatorWalletTransactionRef>(referenceType) is { } parsedReference)
            query = query.Where(item => item.ReferenceType == parsedReference);
        if (normalizedSearch is not null)
        {
            if (Guid.TryParse(normalizedSearch, out var id))
            {
                query = query.Where(item =>
                    item.Id == id
                    || item.ReferenceId == id
                    || item.ReferenceType == OperatorWalletTransactionRef.TRIP_SETTLEMENT
                        && settlements.Any(settlement =>
                            settlement.Id == item.ReferenceId
                            && settlement.TripId == id));
            }
            else
            {
                var prefixPattern = EscapeLike(normalizedSearch) + "%";
                var containsPattern = "%" + EscapeLike(normalizedSearch) + "%";
                query = query.Where(item =>
                    item.Note != null && EF.Functions.ILike(item.Note, containsPattern, "\\")
                    || item.ReferenceType == OperatorWalletTransactionRef.TRIP_SETTLEMENT
                        && settlements.Any(settlement =>
                            settlement.Id == item.ReferenceId
                            && ledger.Any(entry =>
                                entry.TripId == settlement.TripId
                                && entry.ReferenceCode != null
                                && EF.Functions.ILike(entry.ReferenceCode, prefixPattern, "\\"))));
            }
        }
        query = ApplyTransactionDates(query, options);
        var total = await query.LongCountAsync(ct);
        query = (options.SortBy ?? "createdAt", IsAscending(options)) switch
        {
            ("amount", true) => query.OrderBy(item => item.Amount).ThenBy(item => item.Id),
            ("amount", false) => query.OrderByDescending(item => item.Amount).ThenByDescending(item => item.Id),
            (_, true) => query.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id),
            _ => query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id),
        };
        var rows = await query.Skip(Offset(options)).Take(options.PageSize).ToListAsync(ct);
        var settlementIds = rows
            .Where(item => item.ReferenceType == OperatorWalletTransactionRef.TRIP_SETTLEMENT && item.ReferenceId.HasValue)
            .Select(item => item.ReferenceId!.Value)
            .Distinct()
            .ToArray();
        var relatedSettlements = settlementIds.Length == 0
            ? []
            : await _db.OperatorTripSettlements.AsNoTracking()
                .Where(item => item.OperatorId == operatorId && settlementIds.Contains(item.Id))
                .ToListAsync(ct);
        var settlementById = relatedSettlements.ToDictionary(item => item.Id);
        var adjustmentTransactionIds = rows
            .Where(item => item.ReferenceType == OperatorWalletTransactionRef.ADJUSTMENT)
            .Select(item => item.Id)
            .ToArray();
        var adjustmentEntries = adjustmentTransactionIds.Length == 0
            ? []
            : await _db.OperatorLedgerEntries.AsNoTracking()
                .Where(item => item.OperatorId == operatorId
                    && adjustmentTransactionIds.Contains(item.ReferenceId)
                    && item.EntryType == OperatorLedgerEntryType.ADJUSTMENT)
                .ToListAsync(ct);
        var adjustmentByTransaction = adjustmentEntries
            .GroupBy(item => item.ReferenceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.CreatedAt).First());
        var actorFallbacks = await LoadLedgerActorFallbacksAsync(adjustmentEntries, ct);
        var settlementActorFallbacks = await LoadSettlementActorFallbacksAsync(relatedSettlements, ct);
        var items = rows.Select(item => ToWalletTransaction(
            item,
            settlementById,
            adjustmentByTransaction,
            actorFallbacks,
            settlementActorFallbacks)).ToList();
        return PagedResult<WalletTransactionDto>.Create(items, options.Page, options.PageSize, total);
    }

    public Task<PagedResult<SettlementDto>> ListOperatorSettlementsAsync(
        Guid operatorId,
        PageOptions options,
        string? status,
        Guid? tripId,
        CancellationToken ct,
        string? search = null,
        string? dateField = null)
        => ListSettlementsAsync(options, operatorId, status, tripId, false, null, ct, search, dateField);

    public async Task<PagedResult<LedgerEntryDto>> ListOperatorLedgerAsync(
        Guid operatorId,
        PageOptions options,
        Guid? tripId,
        string? entryType,
        string? referenceType,
        CancellationToken ct,
        string? search = null,
        string? dateField = null)
    {
        ValidatePage(options, ["createdAt", "amount"]);
        var normalizedDateField = ValidateDateField(dateField, ["createdAt", "occurredAt"]);
        var normalizedSearch = NormalizeSearch(search);
        var query = _db.OperatorLedgerEntries.AsNoTracking().Where(item => item.OperatorId == operatorId);
        var settlements = _db.OperatorTripSettlements.AsNoTracking()
            .Where(item => item.OperatorId == operatorId);
        if (tripId.HasValue)
            query = query.Where(item => item.TripId == tripId);
        if (ParseOptional<OperatorLedgerEntryType>(entryType) is { } parsedType)
            query = query.Where(item => item.EntryType == parsedType);
        if (ParseOptional<OperatorLedgerReferenceType>(referenceType) is { } parsedReference)
            query = query.Where(item => item.ReferenceType == parsedReference);
        if (normalizedSearch is not null)
        {
            if (Guid.TryParse(normalizedSearch, out var id))
            {
                query = query.Where(item =>
                    item.Id == id
                    || item.ReferenceId == id
                    || item.TripId == id
                    || settlements.Any(settlement =>
                        (settlement.Id == id || settlement.WalletTransactionId == id)
                        && settlement.TripId == item.TripId));
            }
            else
            {
                var prefixPattern = EscapeLike(normalizedSearch) + "%";
                var containsPattern = "%" + EscapeLike(normalizedSearch) + "%";
                query = query.Where(item =>
                    item.ReferenceCode != null && EF.Functions.ILike(item.ReferenceCode, prefixPattern, "\\")
                    || item.Note != null && EF.Functions.ILike(item.Note, containsPattern, "\\"));
            }
        }
        query = ApplyLedgerDates(query, options, normalizedDateField);
        var total = await query.LongCountAsync(ct);
        query = (options.SortBy ?? "createdAt", IsAscending(options)) switch
        {
            ("amount", true) => query.OrderBy(item => item.Amount).ThenBy(item => item.Id),
            ("amount", false) => query.OrderByDescending(item => item.Amount).ThenByDescending(item => item.Id),
            (_, true) => query.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id),
            _ => query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id),
        };
        var rows = await query.Skip(Offset(options)).Take(options.PageSize).ToListAsync(ct);
        var actorFallbacks = await LoadLedgerActorFallbacksAsync(rows, ct);
        var tripIds = rows.Where(item => item.TripId.HasValue).Select(item => item.TripId!.Value).Distinct().ToArray();
        var relatedSettlements = tripIds.Length == 0
            ? []
            : await _db.OperatorTripSettlements.AsNoTracking()
                .Where(item => item.OperatorId == operatorId && tripIds.Contains(item.TripId))
                .ToListAsync(ct);
        var settlementByTrip = relatedSettlements.ToDictionary(item => item.TripId);
        var items = rows.Select(item => ToLedgerEntry(item, actorFallbacks, settlementByTrip)).ToList();
        return PagedResult<LedgerEntryDto>.Create(items, options.Page, options.PageSize, total);
    }

    public async Task<PagedResult<InvoiceListItemDto>> ListInvoicesAsync(
        Guid operatorId, PageOptions options, string? status, CancellationToken ct)
        => await ListInvoicesFilteredAsync(operatorId, options, status, null, ct);

    public async Task<PagedResult<InvoiceListItemDto>> ListInvoicesFilteredAsync(
        Guid operatorId, PageOptions options, string? status, string? search, CancellationToken ct)
    {
        if (options.Page < 1 || options.PageSize is < 1 or > 100
            || options.SortDir is not ("asc" or "desc"))
            throw new CodedValidationException("VALIDATION_ERROR", "Invalid pagination or sort direction.");
        if (options.From.HasValue && options.To.HasValue && options.From > options.To)
            throw new CodedValidationException("VALIDATION_ERROR", "The from date must not be after the to date.");
        if (!string.IsNullOrWhiteSpace(options.SortBy)
            && !new[] { "issuedAt", "createdAt", "amount", "invoiceNumber" }.Contains(options.SortBy))
            throw new BadRequestException("INVALID_SORT_FIELD", $"Unsupported sort field '{options.SortBy}'.");
        var query = _db.Invoices.AsNoTracking().Where(item => item.OperatorId == operatorId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<InvoiceStatus>(status.Trim(), false, out var parsedStatus)
                || !Enum.IsDefined(parsedStatus))
                throw new CodedValidationException("VALIDATION_ERROR", "status is invalid.");
            query = query.Where(item => item.Status == parsedStatus);
        }
        var normalizedSearch = NormalizeOptionalSearch(search);
        if (normalizedSearch is not null)
        {
            var pattern = $"%{EscapeLike(normalizedSearch)}%";
            if (Guid.TryParse(normalizedSearch, out var paymentId))
            {
                query = query.Where(item => EF.Functions.ILike(item.InvoiceNumber, pattern, "\\")
                    || item.PaymentId == paymentId);
            }
            else
            {
                query = query.Where(item => EF.Functions.ILike(item.InvoiceNumber, pattern, "\\"));
            }
        }
        query = ApplyDates(query, options);
        var total = await query.LongCountAsync(ct);
        query = (options.SortBy ?? "createdAt", IsAscending(options)) switch
        {
            ("issuedAt", true) => query.OrderBy(item => item.IssuedAt).ThenBy(item => item.Id),
            ("issuedAt", false) => query.OrderByDescending(item => item.IssuedAt).ThenByDescending(item => item.Id),
            ("amount", true) => query.OrderBy(item => item.Amount).ThenBy(item => item.Id),
            ("amount", false) => query.OrderByDescending(item => item.Amount).ThenByDescending(item => item.Id),
            ("invoiceNumber", true) => query.OrderBy(item => item.InvoiceNumber).ThenBy(item => item.Id),
            ("invoiceNumber", false) => query.OrderByDescending(item => item.InvoiceNumber).ThenByDescending(item => item.Id),
            ("createdAt", true) => query.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id),
            _ => query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id),
        };
        var rows = await query.Skip(Offset(options)).Take(options.PageSize).ToListAsync(ct);
        var items = rows.Select(ToInvoiceListItem).ToList();
        return PagedResult<InvoiceListItemDto>.Create(items, options.Page, options.PageSize, total);
    }

    public async Task<InvoiceDetailDto> GetInvoiceAsync(Guid operatorId, Guid invoiceId, CancellationToken ct)
    {
        var invoice = await _db.Invoices.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == invoiceId && item.OperatorId == operatorId, ct)
            ?? throw NotFound("INVOICE_NOT_FOUND", "Invoice was not found.");
        var context = SubscriptionPaymentContextCodec.DeserializeTrusted(invoice.Metadata ?? "{}");
        return new InvoiceDetailDto(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.PaymentId,
            invoice.Status.ToString(),
            invoice.Amount.Amount,
            context.BillingPeriod,
            invoice.PeriodFrom,
            invoice.PeriodTo,
            invoice.PdfGenerationStatus.ToString(),
            invoice.CreatedAt,
            invoice.IssuedAt,
            context.PlanName,
            context.BuyerSnapshot,
            $"{_operatorWeb.InvoiceDetailBaseUrl.TrimEnd('/')}/{invoice.Id:D}",
            $"{_invoiceStorage.StableBaseUrl.TrimEnd('/')}/v1/operator/invoices/{invoice.Id:D}/download");
    }

    public async Task<PagedResult<AdminSettlementDto>> ListAdminSettlementsAsync(
        PageOptions options, Guid? operatorId, string? status, Guid? tripId, bool stuckOnly, string? severity, CancellationToken ct, string? search = null)
    {
        var page = await LoadSettlementRowsAsync(options, operatorId, status, tripId, stuckOnly, severity, ct, search);
        var (operators, users) = await LoadSettlementFallbacksAsync(page.Rows, ct);
        var highBefore = _clock.UtcNow.AddDays(-21);
        var items = page.Rows.Select(item => new AdminSettlementDto(item.Id, item.TripId, item.OperatorId,
            item.Status.ToString(), item.EligibleAt, item.NetAmount, item.SettlementMethod?.ToString(), item.SettledAt,
            item.CreatedAt, item.SettlementFailureCount, item.ActiveFailureCode,
            item.ActiveFailureCode is null ? null : item.SettlementFailureCount >= 3 || item.EligibleAt < highBefore
                ? "HIGH"
                : "WARNING",
            ToOperator(item, operators),
            ToActor(item, users))).ToList();
        return PagedResult<AdminSettlementDto>.Create(items, options.Page, options.PageSize, page.Total);
    }

    public async Task<PlatformWalletDto> GetPlatformWalletAsync(CancellationToken ct)
    {
        var wallet = await _db.PlatformWallets.AsNoTracking().SingleAsync(ct);
        return new PlatformWalletDto(wallet.Id, wallet.Balance.Amount, wallet.UpdatedAt);
    }

    public async Task<PagedResult<PlatformWalletTransactionDto>> ListPlatformTransactionsAsync(
        PageOptions options, string? type, string? referenceType, CancellationToken ct, string? search = null)
    {
        ValidatePage(options, ["createdAt", "amount"]);
        var query = _db.PlatformWalletTransactions.AsNoTracking().AsQueryable();
        if (ParseOptional<PlatformWalletTransactionType>(type) is { } parsedType)
            query = query.Where(item => item.Type == parsedType);
        if (ParseOptional<PlatformWalletTransactionRef>(referenceType) is { } parsedReference)
            query = query.Where(item => item.ReferenceType == parsedReference);
        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            if (Guid.TryParse(normalizedSearch, out var id))
            {
                query = query.Where(item => item.Id == id || item.ReferenceId == id);
            }
            else
            {
                var pattern = $"%{EscapeLike(normalizedSearch)}%";
                var parsedSearchReference = Enum.TryParse<PlatformWalletTransactionRef>(
                    normalizedSearch,
                    ignoreCase: true,
                    out var searchReference)
                    ? searchReference
                    : (PlatformWalletTransactionRef?)null;
                query = parsedSearchReference.HasValue
                    ? query.Where(item =>
                        (item.Note != null && EF.Functions.ILike(item.Note, pattern, "\\"))
                        || (item.ActorDisplayName != null && EF.Functions.ILike(item.ActorDisplayName, pattern, "\\"))
                        || item.ReferenceType == parsedSearchReference.Value)
                    : query.Where(item =>
                        (item.Note != null && EF.Functions.ILike(item.Note, pattern, "\\"))
                        || (item.ActorDisplayName != null && EF.Functions.ILike(item.ActorDisplayName, pattern, "\\")));
            }
        }
        query = ApplyDates(query, options);
        var total = await query.LongCountAsync(ct);
        query = (options.SortBy ?? "createdAt", IsAscending(options)) switch
        {
            ("amount", true) => query.OrderBy(item => item.Amount),
            ("amount", false) => query.OrderByDescending(item => item.Amount),
            (_, true) => query.OrderBy(item => item.CreatedAt),
            _ => query.OrderByDescending(item => item.CreatedAt),
        };
        var rows = await query.Skip(Offset(options)).Take(options.PageSize).ToListAsync(ct);
        var actorFallbacks = await LoadPlatformActorFallbacksAsync(rows, ct);
        var items = rows.Select(item => new PlatformWalletTransactionDto(item.Id, item.Type.ToString(), item.Amount.Amount,
            item.BalanceBefore.Amount, item.BalanceAfter.Amount, item.ReferenceType.ToString(), item.ReferenceId,
            item.Note, item.CreatedAt, item.ActorType.ToString(), ToActor(item, actorFallbacks))).ToList();
        return PagedResult<PlatformWalletTransactionDto>.Create(items, options.Page, options.PageSize, total);
    }

    public Task<AdjustmentResult> AdjustPlatformWalletAsync(
        AdjustmentRequest request, Guid actorUserId, CancellationToken ct)
        => AdjustPlatformAsUserAsync(request, actorUserId, ct);

    public Task<AdjustmentResult> AdjustOperatorWalletAsync(
        Guid operatorId, AdjustmentRequest request, Guid actorUserId, CancellationToken ct)
        => AdjustOperatorWithRetryAsync(operatorId, request, actorUserId, ct);

    public async Task<ManualSettlementResult> SettleAsync(Guid settlementId, Guid actorUserId, CancellationToken ct)
    {
        var identity = await _db.OperatorTripSettlements.AsNoTracking()
            .Where(item => item.Id == settlementId)
            .Select(item => new { item.TripId, item.OperatorId, item.Status })
            .SingleOrDefaultAsync(ct)
            ?? throw NotFound("TRIP_SETTLEMENT_NOT_FOUND", "Settlement was not found.");
        if (identity.Status is OperatorTripSettlementStatus.SETTLED or OperatorTripSettlementStatus.CANCELLED)
            throw new ConflictException("TRIP_SETTLEMENT_ALREADY_SETTLED", "Settlement is already terminal.");
        var actor = await LoadRequiredActorAsync(actorUserId, ct);
        var result = await _settlements.SettleAsync(
            settlementId,
            OperatorTripSettlementMethod.ADMIN_MANUAL,
            actor,
            true,
            ct)
            ?? throw new ConflictException("TRIP_SETTLEMENT_ALREADY_SETTLED", "Settlement is already terminal.");
        var finalMethod = await _db.OperatorTripSettlements.AsNoTracking()
            .Where(item => item.Id == settlementId)
            .Select(item => item.SettlementMethod)
            .SingleAsync(ct);
        return new ManualSettlementResult(result.SettlementId, identity.TripId, identity.OperatorId,
            result.NetAmount, result.Status, finalMethod?.ToString() ?? OperatorTripSettlementMethod.ADMIN_MANUAL.ToString(),
            result.SettledAt);
    }

    private async Task<PagedResult<SettlementDto>> ListSettlementsAsync(
        PageOptions options,
        Guid? operatorId,
        string? status,
        Guid? tripId,
        bool stuckOnly,
        string? severity,
        CancellationToken ct,
        string? search = null,
        string? dateField = null)
    {
        var page = await LoadSettlementRowsAsync(
            options,
            operatorId,
            status,
            tripId,
            stuckOnly,
            severity,
            ct,
            search,
            dateField);
        var actorFallbacks = await LoadSettlementActorFallbacksAsync(page.Rows, ct);
        var tripIds = page.Rows.Select(item => item.TripId).Distinct().ToArray();
        var projectionByTrip = page.Projections;
        if (projectionByTrip is null)
        {
            var projections = operatorId.HasValue
                ? await _ledger.GetTripFinancialProjectionsAsync(operatorId.Value, tripIds, ct)
                : [];
            projectionByTrip = projections.ToDictionary(item => item.TripId);
        }
        var tripSummaries = await LoadTripSummariesSafeAsync(tripIds, ct);
        var tripById = tripSummaries.ToDictionary(item => item.TripId);
        var now = _clock.UtcNow;
        var items = page.Rows.Select(item => ToSettlement(
            item,
            projectionByTrip.GetValueOrDefault(item.TripId),
            tripById.GetValueOrDefault(item.TripId),
            ToActor(item, actorFallbacks),
            now)).ToList();
        return PagedResult<SettlementDto>.Create(items, options.Page, options.PageSize, page.Total);
    }

    private async Task<IReadOnlyDictionary<Guid, IdentityFinancialUser>> LoadSettlementActorFallbacksAsync(
        IReadOnlyCollection<OperatorTripSettlement> rows, CancellationToken ct)
    {
        var userIds = rows
            .Where(item => item.SettledByUserId.HasValue && !item.SettledBySnapshotResolved)
            .Select(item => item.SettledByUserId!.Value)
            .Distinct()
            .ToArray();
        if (userIds.Length == 0)
            return new Dictionary<Guid, IdentityFinancialUser>();

        var users = await _identity.GetUsersAsync(userIds, ct);
        return users.ToDictionary(item => item.UserId);
    }

    private async Task<IReadOnlyList<TripRevenueSummaryItem>> LoadTripSummariesSafeAsync(
        IReadOnlyList<Guid> tripIds,
        CancellationToken ct)
    {
        if (tripIds.Count == 0)
            return [];

        try
        {
            return await _trips.GetTripSummariesAsync(tripIds, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Trip enrichment is unavailable for {TripCount} operator settlement rows; returning partial financial data.",
                tripIds.Count);
            return [];
        }
    }

    private static WalletTransactionDto ToWalletTransaction(
        OperatorWalletTransaction item,
        IReadOnlyDictionary<Guid, OperatorTripSettlement> settlements,
        IReadOnlyDictionary<Guid, OperatorLedgerEntry> adjustmentEntries,
        IReadOnlyDictionary<Guid, IdentityFinancialUser> actorFallbacks,
        IReadOnlyDictionary<Guid, IdentityFinancialUser> settlementActorFallbacks)
    {
        var missing = new List<string>();
        RelatedSettlementDto? relatedSettlement = null;
        OperatorTripSettlement? relatedSettlementEntity = null;
        if (item.ReferenceType == OperatorWalletTransactionRef.TRIP_SETTLEMENT)
        {
            if (!item.ReferenceId.HasValue || !settlements.TryGetValue(item.ReferenceId.Value, out var settlement))
            {
                missing.Add("relatedSettlement");
            }
            else
            {
                relatedSettlementEntity = settlement;
                var method = settlement.SettlementMethod?.ToString();
                if (method is null)
                {
                    method = "UNKNOWN";
                    missing.Add("relatedSettlement.method");
                }
                relatedSettlement = new RelatedSettlementDto(settlement.Id, settlement.TripId, method);
            }
        }

        adjustmentEntries.TryGetValue(item.Id, out var adjustment);
        var actor = adjustment is not null
            ? ToActor(adjustment, actorFallbacks)
            : relatedSettlementEntity is not null
                ? ToActor(relatedSettlementEntity, settlementActorFallbacks)
                : null;
        if (item.ReferenceType == OperatorWalletTransactionRef.ADJUSTMENT)
        {
            if (adjustment is null)
                missing.Add("adjustmentLedger");
            else if (adjustment.ActorType == FinancialActorType.USER && actor is null)
                missing.Add("actor");
        }
        if (relatedSettlementEntity?.SettledByUserId is not null && actor is null)
            missing.Add("actor");

        return new WalletTransactionDto(
            item.Id,
            item.Type.ToString(),
            item.Amount.Amount,
            item.BalanceBefore.Amount,
            item.BalanceAfter.Amount,
            item.ReferenceType.ToString(),
            item.ReferenceId,
            item.Note,
            item.CreatedAt,
            SignedAmount: item.Type == OperatorWalletTransactionType.CREDIT
                ? item.Amount.Amount
                : -item.Amount.Amount,
            Currency: "VND",
            RelatedSettlement: relatedSettlement,
            ActorType: adjustment?.ActorType.ToString()
                ?? (relatedSettlementEntity?.SettledByUserId is null
                    ? FinancialActorType.SYSTEM.ToString()
                    : FinancialActorType.USER.ToString()),
            Actor: actor,
            AdjustmentReason: adjustment?.AdjustmentReason?.ToString(),
            DataCompleteness: missing.Count == 0 ? "COMPLETE" : "PARTIAL",
            MissingFields: missing);
    }

    private static LedgerEntryDto ToLedgerEntry(
        OperatorLedgerEntry item,
        IReadOnlyDictionary<Guid, IdentityFinancialUser> actorFallbacks,
        IReadOnlyDictionary<Guid, OperatorTripSettlement> settlements)
    {
        var missing = new List<string>();
        if (item.ReferenceType is OperatorLedgerReferenceType.BOOKING or OperatorLedgerReferenceType.PARCEL
            && item.ReferenceCode is null)
        {
            missing.Add("referenceCode");
        }
        if (!item.OccurredAt.HasValue)
            missing.Add("occurredAt");
        if (item.EntryType == OperatorLedgerEntryType.VOUCHER_OPERATOR_FUNDED_AUDIT
            && !item.OperatorFundedVoucherAmount.HasValue)
        {
            missing.Add("operatorFundedVoucherAmount");
        }

        var actor = ToActor(item, actorFallbacks);
        if (item.ActorType == FinancialActorType.USER && actor is null)
            missing.Add("actor");
        LedgerSettlementDto? settlement = null;
        if (item.TripId.HasValue && settlements.TryGetValue(item.TripId.Value, out var related))
        {
            settlement = new LedgerSettlementDto(
                related.Id,
                related.Status.ToString(),
                related.EligibleAt,
                related.SettledAt,
                related.WalletTransactionId);
        }

        var affectsRevenue = IsCanonicalFinancialEntry(item);
        return new LedgerEntryDto(
            item.Id,
            item.TripId,
            item.EntryType.ToString(),
            item.Amount,
            item.ReferenceType.ToString(),
            item.ReferenceId,
            item.CreatedAt,
            item.Note,
            item.ActorType.ToString(),
            actor,
            ReferenceCode: item.ReferenceCode,
            OccurredAt: item.OccurredAt ?? item.CreatedAt,
            OccurredAtSource: item.OccurredAt.HasValue
                ? "BUSINESS_EVENT"
                : "LEDGER_CREATED_AT_FALLBACK",
            OperatorFundedVoucherAmount: item.OperatorFundedVoucherAmount,
            AdjustmentReason: item.AdjustmentReason?.ToString(),
            AffectsRevenue: affectsRevenue,
            AffectsSettlement: affectsRevenue && item.TripId.HasValue,
            Settlement: settlement,
            DataCompleteness: missing.Count == 0 ? "COMPLETE" : "PARTIAL",
            MissingFields: missing);
    }

    private static SettlementDto ToSettlement(
        OperatorTripSettlement item,
        TripFinancialProjection? projection,
        TripRevenueSummaryItem? trip,
        FinancialActorDto? actor,
        DateTimeOffset now)
    {
        var financial = projection is null
            ? new SettlementFinancialBreakdownDto(0, 0, 0, 0, 0, 0, 0)
            : new SettlementFinancialBreakdownDto(
                projection.GrossSalesAmount,
                projection.PassengerPaidAmount,
                projection.VietRideFundedAmount,
                projection.OperatorFundedDiscountAmount,
                projection.RefundAmount,
                projection.RecognizedAdjustmentAmount,
                projection.NetEntitlementAmount);
        var processingState = item.Status switch
        {
            OperatorTripSettlementStatus.PENDING_HOLD => "ON_HOLD",
            OperatorTripSettlementStatus.ELIGIBLE when item.ActiveFailureCode is not null => "RETRY_SCHEDULED",
            OperatorTripSettlementStatus.ELIGIBLE => "READY_FOR_SETTLEMENT",
            OperatorTripSettlementStatus.SETTLED => "COMPLETED",
            OperatorTripSettlementStatus.CANCELLED => "CANCELLED",
            _ => "ON_HOLD",
        };
        var nextScheduledAttempt = TripSettlementSchedule.GetNextScheduledAttemptAt(
            item.Status,
            item.EligibleAt,
            now);

        return new SettlementDto(
            item.Id,
            item.TripId,
            item.Status.ToString(),
            item.EligibleAt,
            financial.NetEntitlementAmount,
            item.SettlementMethod?.ToString(),
            item.SettledAt,
            item.CreatedAt,
            actor,
            TripTerminalAt: item.TripTerminalAt,
            WalletTransactionId: item.WalletTransactionId,
            FinancialBreakdown: financial,
            ProcessingState: processingState,
            NextScheduledSettlementAttemptAt: nextScheduledAttempt,
            DelayReason: item.ActiveFailureCode is null ? null : "SYSTEM_PROCESSING_DELAY",
            AttemptCount: item.SettlementFailureCount,
            LastAttemptAt: item.LastSettlementFailureAt,
            NextRetryAt: item.ActiveFailureCode is null
                ? null
                : TripSettlementSchedule.GetNextAutoSettlementAfter(now),
            CancelReason: item.Status == OperatorTripSettlementStatus.CANCELLED
                ? "NON_POSITIVE_NET_ENTITLEMENT"
                : null,
            Trip: trip is null
                ? null
                : new SettlementTripDto(
                    trip.DepartureAt,
                    trip.RouteId,
                    trip.RouteName,
                    trip.OriginName,
                    trip.DestinationName),
            DataCompleteness: trip is not null && projection?.MetadataComplete == true
                ? "COMPLETE"
                : "PARTIAL");
    }

    private static bool IsCanonicalFinancialEntry(OperatorLedgerEntry item)
    {
        var isSupportedReference = item.ReferenceType is OperatorLedgerReferenceType.BOOKING
            or OperatorLedgerReferenceType.PARCEL;
        if (!isSupportedReference)
            return false;
        if (item.EntryType is OperatorLedgerEntryType.BOOKING_REVENUE
            or OperatorLedgerEntryType.PARCEL_REVENUE
            or OperatorLedgerEntryType.BOOKING_REFUND
            or OperatorLedgerEntryType.PARCEL_REFUND
            or OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT)
        {
            return item.ReferenceType == OperatorLedgerReferenceType.BOOKING
                ? item.EntryType is OperatorLedgerEntryType.BOOKING_REVENUE
                    or OperatorLedgerEntryType.BOOKING_REFUND
                    or OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT
                : item.EntryType is OperatorLedgerEntryType.PARCEL_REVENUE
                    or OperatorLedgerEntryType.PARCEL_REFUND
                    or OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT;
        }

        return item.EntryType == OperatorLedgerEntryType.ADJUSTMENT
            && item.AdjustmentReason == OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL;
    }

    private async Task<SettlementPageRows> LoadSettlementRowsAsync(
        PageOptions options,
        Guid? operatorId,
        string? status,
        Guid? tripId,
        bool stuckOnly,
        string? severity,
        CancellationToken ct,
        string? search = null,
        string? dateField = null)
    {
        ValidatePage(options, ["createdAt", "eligibleAt", "settledAt", "netAmount"]);
        var normalizedDateField = ValidateDateField(
            dateField,
            ["createdAt", "tripTerminalAt", "eligibleAt", "settledAt"]);
        var normalizedSearch = NormalizeSearch(search);
        var query = _db.OperatorTripSettlements.AsNoTracking().AsQueryable();
        if (operatorId.HasValue)
            query = query.Where(item => item.OperatorId == operatorId);
        if (tripId.HasValue)
            query = query.Where(item => item.TripId == tripId);
        if (ParseOptional<OperatorTripSettlementStatus>(status) is { } parsedStatus)
            query = query.Where(item => item.Status == parsedStatus);
        if (stuckOnly)
            query = query.Where(item => item.Status == OperatorTripSettlementStatus.ELIGIBLE && item.ActiveFailureCode != null);
        var highOnly = string.Equals(severity, "HIGH", StringComparison.Ordinal);
        var warningOnly = string.Equals(severity, "WARNING", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(severity) && !highOnly && !warningOnly)
            throw new BadRequestException("INVALID_FILTER", "Unsupported settlement severity.");
        var highBefore = _clock.UtcNow.AddDays(-21);
        if (highOnly)
            query = query.Where(item => item.Status == OperatorTripSettlementStatus.ELIGIBLE
                && item.ActiveFailureCode != null
                && (item.SettlementFailureCount >= 3 || item.EligibleAt < highBefore));
        if (warningOnly)
            query = query.Where(item => item.Status == OperatorTripSettlementStatus.ELIGIBLE
                && item.ActiveFailureCode != null
                && item.SettlementFailureCount < 3
                && item.EligibleAt >= highBefore);
        if (normalizedSearch is not null)
        {
            if (Guid.TryParse(normalizedSearch, out var id))
            {
                query = query.Where(item => item.Id == id || item.TripId == id);
            }
            else
            {
                var prefixPattern = EscapeLike(normalizedSearch) + "%";
                var scopedLedger = _db.OperatorLedgerEntries.AsNoTracking();
                if (operatorId.HasValue)
                    scopedLedger = scopedLedger.Where(item => item.OperatorId == operatorId.Value);
                var containsPattern = $"%{EscapeLike(normalizedSearch)}%";
                query = query.Where(item =>
                    (item.OperatorName != null && EF.Functions.ILike(item.OperatorName, containsPattern, "\\"))
                    || (item.ActiveFailureCode != null && EF.Functions.ILike(item.ActiveFailureCode, containsPattern, "\\"))
                    || scopedLedger.Any(entry =>
                        entry.TripId == item.TripId
                        && entry.ReferenceCode != null
                        && EF.Functions.ILike(entry.ReferenceCode, prefixPattern, "\\")));
            }
        }
        query = ApplySettlementDates(query, options, normalizedDateField);
        var total = await query.LongCountAsync(ct);
        if (operatorId.HasValue && options.SortBy == "netAmount")
        {
            var projectionQuery = CanonicalTripFinancialProjectionQuery.ForOperator(_db, operatorId.Value);
            var rowsWithProjection =
                from settlement in query
                join projection in projectionQuery on settlement.TripId equals projection.TripId into projectionGroup
                from projection in projectionGroup.DefaultIfEmpty()
                select new { Settlement = settlement, Projection = projection };
            var orderedQuery = IsAscending(options)
                ? rowsWithProjection
                    .OrderBy(item => (long?)item.Projection.NetEntitlementAmount ?? 0)
                    .ThenBy(item => item.Settlement.Id)
                : rowsWithProjection
                    .OrderByDescending(item => (long?)item.Projection.NetEntitlementAmount ?? 0)
                    .ThenByDescending(item => item.Settlement.Id);
            var pageRows = await orderedQuery
                .Skip(Offset(options))
                .Take(options.PageSize)
                .ToListAsync(ct);
            var projectionByTrip = pageRows
                .Where(item => item.Projection is not null)
                .ToDictionary(item => item.Settlement.TripId, item => item.Projection!);
            return new SettlementPageRows(
                pageRows.Select(item => item.Settlement).ToList(),
                total,
                projectionByTrip);
        }
        query = (options.SortBy ?? "createdAt", IsAscending(options)) switch
        {
            ("eligibleAt", true) => query.OrderBy(item => item.EligibleAt).ThenBy(item => item.Id),
            ("eligibleAt", false) => query.OrderByDescending(item => item.EligibleAt).ThenByDescending(item => item.Id),
            ("settledAt", true) => query.OrderBy(item => item.SettledAt).ThenBy(item => item.Id),
            ("settledAt", false) => query.OrderByDescending(item => item.SettledAt).ThenByDescending(item => item.Id),
            ("netAmount", true) => query.OrderBy(item => item.NetAmount).ThenBy(item => item.Id),
            ("netAmount", false) => query.OrderByDescending(item => item.NetAmount).ThenByDescending(item => item.Id),
            ("createdAt", true) => query.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id),
            _ => query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id),
        };
        var rows = await query.Skip(Offset(options)).Take(options.PageSize).ToListAsync(ct);
        return new SettlementPageRows(rows, total);
    }

    private async Task<(IReadOnlyDictionary<Guid, IdentityFinancialOperator> Operators, IReadOnlyDictionary<Guid, IdentityFinancialUser> Users)>
        LoadSettlementFallbacksAsync(IReadOnlyCollection<OperatorTripSettlement> rows, CancellationToken ct)
    {
        var operatorIds = rows
            .Where(item => !item.OperatorSnapshotResolved)
            .Select(item => item.OperatorId)
            .Distinct()
            .ToArray();
        var userIds = rows
            .Where(item => item.SettledByUserId.HasValue && !item.SettledBySnapshotResolved)
            .Select(item => item.SettledByUserId!.Value)
            .Distinct()
            .ToArray();

        var operatorTask = operatorIds.Length == 0
            ? Task.FromResult<IReadOnlyList<IdentityFinancialOperator>>([])
            : _identity.GetOperatorsAsync(operatorIds, ct);
        var userTask = userIds.Length == 0
            ? Task.FromResult<IReadOnlyList<IdentityFinancialUser>>([])
            : _identity.GetUsersAsync(userIds, ct);
        await Task.WhenAll(operatorTask, userTask);
        return (
            (await operatorTask).ToDictionary(item => item.OperatorId),
            (await userTask).ToDictionary(item => item.UserId));
    }

    private async Task<FinancialActorSnapshot> LoadRequiredActorAsync(Guid actorUserId, CancellationToken ct)
    {
        var users = await _identity.GetUsersAsync([actorUserId], ct);
        var actor = users.SingleOrDefault(item => item.UserId == actorUserId);
        if (actor is null
            || actor.Deleted
            || !string.Equals(actor.Role, "SYSTEM_ADMIN", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(actor.Email))
        {
            throw new UpstreamUnavailableException();
        }

        return new FinancialActorSnapshot(actor.UserId, actor.DisplayName, actor.Email, actor.Role);
    }

    private async Task<IReadOnlyDictionary<Guid, IdentityFinancialUser>> LoadPlatformActorFallbacksAsync(
        IReadOnlyCollection<PlatformWalletTransaction> rows,
        CancellationToken ct)
    {
        var userIds = rows
            .Where(item => !item.ActorSnapshotResolved && item.ActorUserId.HasValue)
            .Select(item => item.ActorUserId!.Value)
            .Distinct()
            .ToArray();
        if (userIds.Length == 0)
            return new Dictionary<Guid, IdentityFinancialUser>();

        var users = await _identity.GetUsersAsync(userIds, ct);
        return users.ToDictionary(item => item.UserId);
    }

    private async Task<IReadOnlyDictionary<Guid, IdentityFinancialUser>> LoadLedgerActorFallbacksAsync(
        IReadOnlyCollection<OperatorLedgerEntry> rows, CancellationToken ct)
    {
        var userIds = rows
            .Where(item => item.ActorType == FinancialActorType.USER
                && item.ActorUserId.HasValue
                && !item.ActorSnapshotResolved)
            .Select(item => item.ActorUserId!.Value)
            .Distinct()
            .ToArray();
        if (userIds.Length == 0)
            return new Dictionary<Guid, IdentityFinancialUser>();

        var users = await _identity.GetUsersAsync(userIds, ct);
        return users.ToDictionary(item => item.UserId);
    }

    private async Task<AdjustmentResult> AdjustPlatformAsUserAsync(
        AdjustmentRequest request,
        Guid actorUserId,
        CancellationToken ct)
    {
        ValidateAdjustment(request);
        var actor = await LoadRequiredActorAsync(actorUserId, ct);
        return await AdjustPlatformWithRetryAsync(request, actor, ct);
    }

    private async Task<AdjustmentResult> AdjustPlatformWithRetryAsync(
        AdjustmentRequest request, FinancialActorSnapshot actor, CancellationToken ct)
    {
        var type = Enum.Parse<PlatformWalletTransactionType>(request.Type, false);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                if (await _actorPrivacy.IsDeletedWithLockAsync(actor.UserId, ct))
                    throw new UnauthorizedAccessException("Financial actor account is deleted.");

                var amount = Money.FromRaw(request.Amount);
                PlatformWalletTransaction movement;
                try
                {
                    movement = type == PlatformWalletTransactionType.CREDIT
                        ? await _platformWallets.CreditAsync(amount, PlatformWalletTransactionRef.MANUAL_ADJUSTMENT, null, request.Note, ct)
                        : await _platformWallets.DebitAsync(amount, PlatformWalletTransactionRef.MANUAL_ADJUSTMENT, null, request.Note, ct);
                }
                catch (InvalidOperationException exception)
                {
                    throw new PlatformWalletInsufficientBalanceException(exception.Message);
                }
                movement.AssignUserActor(actor);
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                _logger.LogInformation("Platform wallet adjusted by admin {ActorUserId}; type {Type}, amount {Amount}.", actor.UserId, type, request.Amount);
                return ToAdjustment(movement, request.Note);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                if (attempt == 2)
                    break;
            }
        }
        throw new ConflictException("WALLET_CONCURRENT_UPDATE", "Wallet was updated concurrently; retry the request.");
    }

    private static FinancialOperatorDto? ToOperator(
        OperatorTripSettlement settlement,
        IReadOnlyDictionary<Guid, IdentityFinancialOperator> fallbacks)
    {
        if (settlement.OperatorSnapshotResolved)
        {
            return string.IsNullOrWhiteSpace(settlement.OperatorName)
                ? null
                : new FinancialOperatorDto(
                    settlement.OperatorId,
                    settlement.OperatorName,
                    settlement.OperatorLogoUrl,
                    settlement.OperatorContactPhone);
        }

        return fallbacks.TryGetValue(settlement.OperatorId, out var item)
            ? new FinancialOperatorDto(item.OperatorId, item.Name, item.LogoUrl, item.ContactPhone)
            : null;
    }

    private static FinancialActorDto? ToActor(
        OperatorLedgerEntry entry,
        IReadOnlyDictionary<Guid, IdentityFinancialUser> fallbacks)
    {
        if (entry.ActorType != FinancialActorType.USER || !entry.ActorUserId.HasValue)
            return null;
        if (entry.ActorSnapshotResolved)
        {
            return CreateActorDto(
                entry.ActorUserId.Value,
                entry.ActorDisplayName,
                entry.ActorEmail,
                entry.ActorRole);
        }

        return fallbacks.TryGetValue(entry.ActorUserId.Value, out var item) && !item.Deleted
            ? CreateActorDto(item.UserId, item.DisplayName, item.Email, item.Role)
            : null;
    }

    private static FinancialActorDto? ToActor(
        OperatorTripSettlement settlement,
        IReadOnlyDictionary<Guid, IdentityFinancialUser> fallbacks)
    {
        if (!settlement.SettledByUserId.HasValue)
            return null;
        if (settlement.SettledBySnapshotResolved)
        {
            return CreateActorDto(
                settlement.SettledByUserId.Value,
                settlement.SettledByDisplayName,
                settlement.SettledByEmail,
                settlement.SettledByRole);
        }

        return fallbacks.TryGetValue(settlement.SettledByUserId.Value, out var item) && !item.Deleted
            ? CreateActorDto(item.UserId, item.DisplayName, item.Email, item.Role)
            : null;
    }

    private static FinancialActorDto? ToActor(
        PlatformWalletTransaction transaction,
        IReadOnlyDictionary<Guid, IdentityFinancialUser> fallbacks)
    {
        if (!transaction.ActorUserId.HasValue)
            return null;
        if (transaction.ActorSnapshotResolved)
        {
            return CreateActorDto(
                transaction.ActorUserId.Value,
                transaction.ActorDisplayName,
                transaction.ActorEmail,
                transaction.ActorRole);
        }

        return fallbacks.TryGetValue(transaction.ActorUserId.Value, out var item) && !item.Deleted
            ? CreateActorDto(item.UserId, item.DisplayName, item.Email, item.Role)
            : null;
    }

    private static FinancialActorDto? CreateActorDto(
        Guid userId,
        string? displayName,
        string? email,
        string? role)
        => string.IsNullOrWhiteSpace(displayName)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(role)
            ? null
            : new FinancialActorDto(userId, displayName, email, role);

    private async Task<AdjustmentResult> AdjustOperatorWithRetryAsync(
        Guid operatorId, AdjustmentRequest request, Guid actorUserId, CancellationToken ct)
    {
        ValidateAdjustment(request);
        var actor = await LoadRequiredActorAsync(actorUserId, ct);
        var type = Enum.Parse<OperatorWalletTransactionType>(request.Type, false);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            if (await _actorPrivacy.IsDeletedWithLockAsync(actor.UserId, ct))
                throw new UnauthorizedAccessException("Financial actor account is deleted.");
            var wallet = await _db.OperatorWallets.AsNoTracking().SingleOrDefaultAsync(item => item.OperatorId == operatorId, ct)
                ?? throw NotFound("RESOURCE_NOT_FOUND", "Operator wallet was not found.");
            var before = wallet.Balance.Amount;
            var after = type == OperatorWalletTransactionType.CREDIT ? checked(before + request.Amount) : before - request.Amount;
            if (after < 0)
                throw new WalletInsufficientBalanceException();
            var affected = await _db.OperatorWallets
                .Where(item => item.OperatorId == operatorId && item.RowVersion == wallet.RowVersion)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Balance, Money.FromRaw(after))
                    .SetProperty(item => item.RowVersion, item => item.RowVersion + 1)
                    .SetProperty(item => item.UpdatedAt, _clock.UtcNow), ct);
            if (affected == 0)
            {
                await transaction.RollbackAsync(ct);
                continue;
            }
            var movement = OperatorWalletTransaction.Create(operatorId, type, Money.FromRaw(request.Amount),
                Money.FromRaw(before), Money.FromRaw(after), OperatorWalletTransactionRef.ADJUSTMENT, null, request.Note);
            await _db.OperatorWalletTransactions.AddAsync(movement, ct);
            var signedAmount = type == OperatorWalletTransactionType.CREDIT ? request.Amount : -request.Amount;
            await _db.OperatorLedgerEntries.AddAsync(OperatorLedgerEntry.Create(operatorId, null,
                OperatorLedgerEntryType.ADJUSTMENT, signedAmount, OperatorLedgerReferenceType.MANUAL,
                movement.Id, movement.Id, request.Note, actor,
                OperatorLedgerAdjustmentReason.MANUAL_WALLET_ADJUSTMENT,
                occurredAt: _clock.UtcNow), ct);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            _logger.LogInformation("Operator wallet {OperatorId} adjusted by admin {ActorUserId}; type {Type}, amount {Amount}.", operatorId, actorUserId, type, request.Amount);
            return ToAdjustment(movement, request.Note);
        }
        throw new ConflictException("WALLET_CONCURRENT_UPDATE", "Wallet was updated concurrently; retry the request.");
    }

    private static InvoiceListItemDto ToInvoiceListItem(Invoice invoice)
    {
        var context = SubscriptionPaymentContextCodec.DeserializeTrusted(invoice.Metadata ?? "{}");
        return new InvoiceListItemDto(invoice.Id, invoice.InvoiceNumber, invoice.PaymentId, invoice.Status.ToString(),
            invoice.Amount.Amount, context.BillingPeriod, invoice.PeriodFrom, invoice.PeriodTo,
            invoice.PdfGenerationStatus.ToString(), invoice.CreatedAt, invoice.IssuedAt);
    }

    private static AdjustmentResult ToAdjustment(PlatformWalletTransaction item, string note)
        => new(item.Id, item.Type.ToString(), item.Amount.Amount, item.BalanceBefore.Amount, item.BalanceAfter.Amount,
            item.ReferenceType.ToString(), item.ReferenceId, note, item.CreatedAt);

    private static AdjustmentResult ToAdjustment(OperatorWalletTransaction item, string note)
        => new(item.Id, item.Type.ToString(), item.Amount.Amount, item.BalanceBefore.Amount, item.BalanceAfter.Amount,
            item.ReferenceType.ToString(), item.ReferenceId, note, item.CreatedAt);

    private static long SumCurrentEntitlement(
        IReadOnlyCollection<OperatorTripSettlement> settlements,
        IReadOnlyDictionary<Guid, TripFinancialProjection> projections)
        => settlements.Sum(item => projections.GetValueOrDefault(item.TripId)?.NetEntitlementAmount ?? 0);

    private static string? NormalizeSearch(string? search)
    {
        if (search is null)
            return null;
        var normalized = search.Trim();
        if (normalized.Length is < 2 or > 100)
            throw new BadRequestException("INVALID_FILTER", "Search must contain between 2 and 100 characters.");
        return normalized;
    }

    private static string? NormalizeOptionalSearch(string? search)
    {
        var normalized = search?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        if (normalized.Length > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "Search must not exceed 100 characters.");
        return normalized;
    }

    private static string ValidateDateField(string? dateField, IReadOnlyCollection<string> supported)
    {
        var normalized = string.IsNullOrWhiteSpace(dateField) ? "createdAt" : dateField.Trim();
        if (!supported.Contains(normalized))
            throw new BadRequestException("INVALID_FILTER", $"Unsupported dateField value '{dateField}'.");
        return normalized;
    }

    private static string EscapeLike(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static IQueryable<OperatorWalletTransaction> ApplyTransactionDates(
        IQueryable<OperatorWalletTransaction> query,
        PageOptions options)
    {
        if (options.From.HasValue)
            query = query.Where(item => item.CreatedAt >= options.From.Value);
        if (options.To.HasValue)
            query = query.Where(item => item.CreatedAt <= options.To.Value);
        return query;
    }

    private static IQueryable<OperatorLedgerEntry> ApplyLedgerDates(
        IQueryable<OperatorLedgerEntry> query,
        PageOptions options,
        string dateField)
    {
        if (options.From.HasValue)
        {
            query = dateField == "occurredAt"
                ? query.Where(item => (item.OccurredAt ?? item.CreatedAt) >= options.From.Value)
                : query.Where(item => item.CreatedAt >= options.From.Value);
        }
        if (options.To.HasValue)
        {
            query = dateField == "occurredAt"
                ? query.Where(item => (item.OccurredAt ?? item.CreatedAt) <= options.To.Value)
                : query.Where(item => item.CreatedAt <= options.To.Value);
        }
        return query;
    }

    private static IQueryable<OperatorTripSettlement> ApplySettlementDates(
        IQueryable<OperatorTripSettlement> query,
        PageOptions options,
        string dateField)
    {
        if (options.From.HasValue)
        {
            query = dateField switch
            {
                "tripTerminalAt" => query.Where(item => item.TripTerminalAt >= options.From.Value),
                "eligibleAt" => query.Where(item => item.EligibleAt >= options.From.Value),
                "settledAt" => query.Where(item => item.SettledAt >= options.From.Value),
                _ => query.Where(item => item.CreatedAt >= options.From.Value),
            };
        }
        if (options.To.HasValue)
        {
            query = dateField switch
            {
                "tripTerminalAt" => query.Where(item => item.TripTerminalAt <= options.To.Value),
                "eligibleAt" => query.Where(item => item.EligibleAt <= options.To.Value),
                "settledAt" => query.Where(item => item.SettledAt <= options.To.Value),
                _ => query.Where(item => item.CreatedAt <= options.To.Value),
            };
        }
        return query;
    }

    private static void ValidateAdjustment(AdjustmentRequest request)
    {
        if (request.Type is not ("CREDIT" or "DEBIT")
            || request.Amount <= 0
            || string.IsNullOrWhiteSpace(request.Note)
            || request.Note.Length > 500)
            throw new CodedValidationException("VALIDATION_ERROR", "Adjustment type, positive amount, and note are required.");
    }

    private static void ValidatePage(PageOptions options, IReadOnlyCollection<string> sortFields)
    {
        if (options.Page < 1 || options.PageSize is < 1 or > 100 || options.SortDir is not ("asc" or "desc"))
            throw new BadRequestException("VALIDATION_ERROR", "Invalid pagination or sort direction.");
        if (!string.IsNullOrWhiteSpace(options.SortBy) && !sortFields.Contains(options.SortBy))
            throw new BadRequestException("INVALID_SORT_FIELD", $"Unsupported sort field '{options.SortBy}'.");
        if (options.From.HasValue && options.To.HasValue && options.From > options.To)
            throw new BadRequestException("VALIDATION_ERROR", "The from date must not be after the to date.");
    }

    private static TEnum? ParseOptional<TEnum>(string? value) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Enum.TryParse<TEnum>(value, false, out var parsed))
            throw new BadRequestException("INVALID_FILTER", $"Unsupported {typeof(TEnum).Name} value '{value}'.");
        return parsed;
    }

    private static int Offset(PageOptions options) => checked((options.Page - 1) * options.PageSize);
    private static bool IsAscending(PageOptions options) => options.SortDir == "asc";
    private static CodedNotFoundException NotFound(string code, string message) => new(code, message);

    private sealed record SettlementPageRows(
        IReadOnlyList<OperatorTripSettlement> Rows,
        long Total,
        IReadOnlyDictionary<Guid, TripFinancialProjection>? Projections = null);

    private static IQueryable<T> ApplyDates<T>(IQueryable<T> query, PageOptions options) where T : BaseEntity<Guid>
    {
        if (options.From.HasValue)
            query = query.Where(item => item.CreatedAt >= options.From.Value);
        if (options.To.HasValue)
            query = query.Where(item => item.CreatedAt <= options.To.Value);
        return query;
    }
}
