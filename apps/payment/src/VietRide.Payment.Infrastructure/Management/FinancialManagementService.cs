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
using VietRide.Payment.Application.Features.Settlements.SettleTrip;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Domain.ValueObjects;
using VietRide.Payment.Infrastructure.Invoices;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.Management;

internal sealed class FinancialManagementService : IFinancialManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PaymentDbContext _db;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IIdentityFinancialProjectionClient _identity;
    private readonly IFinancialActorPrivacyStore _actorPrivacy;
    private readonly TripSettlementService _settlements;
    private readonly OperatorWebOptions _operatorWeb;
    private readonly InvoiceStorageOptions _invoiceStorage;
    private readonly IClock _clock;
    private readonly ILogger<FinancialManagementService> _logger;

    public FinancialManagementService(
        PaymentDbContext db,
        IPlatformWalletRepository platformWallets,
        IIdentityFinancialProjectionClient identity,
        IFinancialActorPrivacyStore actorPrivacy,
        TripSettlementService settlements,
        IOptions<OperatorWebOptions> operatorWeb,
        IOptions<InvoiceStorageOptions> invoiceStorage,
        IClock clock,
        ILogger<FinancialManagementService> logger)
    {
        _db = db;
        _platformWallets = platformWallets;
        _identity = identity;
        _actorPrivacy = actorPrivacy;
        _settlements = settlements;
        _operatorWeb = operatorWeb.Value;
        _invoiceStorage = invoiceStorage.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<OperatorWalletDto> GetOperatorWalletAsync(Guid operatorId, CancellationToken ct)
    {
        var wallet = await _db.OperatorWallets.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperatorId == operatorId, ct)
            ?? throw NotFound("OPERATOR_WALLET_NOT_FOUND", "Operator wallet was not found.");
        var pendingTripIds = _db.OperatorTripSettlements.AsNoTracking()
            .Where(item => item.OperatorId == operatorId && item.Status == OperatorTripSettlementStatus.PENDING_HOLD)
            .Select(item => item.TripId);
        var pending = await _db.OperatorLedgerEntries.AsNoTracking()
            .Where(item => item.OperatorId == operatorId
                && item.TripId.HasValue
                && pendingTripIds.Contains(item.TripId.Value))
            .SumAsync(item => (long?)item.Amount, ct) ?? 0;
        var eligible = await _db.OperatorTripSettlements.AsNoTracking()
            .Where(item => item.OperatorId == operatorId && item.Status == OperatorTripSettlementStatus.ELIGIBLE)
            .SumAsync(item => (long?)item.NetAmount, ct) ?? 0;
        return new OperatorWalletDto(operatorId, wallet.Balance.Amount, Math.Max(0, pending), eligible, wallet.UpdatedAt);
    }

    public async Task<PagedResult<WalletTransactionDto>> ListOperatorTransactionsAsync(
        Guid operatorId, PageOptions options, string? type, string? referenceType, CancellationToken ct)
    {
        ValidatePage(options, ["createdAt", "amount"]);
        var query = _db.OperatorWalletTransactions.AsNoTracking().Where(item => item.OperatorId == operatorId);
        if (ParseOptional<OperatorWalletTransactionType>(type) is { } parsedType)
            query = query.Where(item => item.Type == parsedType);
        if (ParseOptional<OperatorWalletTransactionRef>(referenceType) is { } parsedReference)
            query = query.Where(item => item.ReferenceType == parsedReference);
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
        var items = rows.Select(item => new WalletTransactionDto(item.Id, item.Type.ToString(), item.Amount.Amount,
            item.BalanceBefore.Amount, item.BalanceAfter.Amount, item.ReferenceType.ToString(), item.ReferenceId,
            item.Note, item.CreatedAt)).ToList();
        return PagedResult<WalletTransactionDto>.Create(items, options.Page, options.PageSize, total);
    }

    public Task<PagedResult<SettlementDto>> ListOperatorSettlementsAsync(
        Guid operatorId, PageOptions options, string? status, Guid? tripId, CancellationToken ct)
        => ListSettlementsAsync(options, operatorId, status, tripId, false, null, ct);

    public async Task<PagedResult<LedgerEntryDto>> ListOperatorLedgerAsync(
        Guid operatorId, PageOptions options, Guid? tripId, string? entryType, string? referenceType, CancellationToken ct)
    {
        ValidatePage(options, ["createdAt", "amount"]);
        var query = _db.OperatorLedgerEntries.AsNoTracking().Where(item => item.OperatorId == operatorId);
        if (tripId.HasValue)
            query = query.Where(item => item.TripId == tripId);
        if (ParseOptional<OperatorLedgerEntryType>(entryType) is { } parsedType)
            query = query.Where(item => item.EntryType == parsedType);
        if (ParseOptional<OperatorLedgerReferenceType>(referenceType) is { } parsedReference)
            query = query.Where(item => item.ReferenceType == parsedReference);
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
        var items = rows.Select(item => new LedgerEntryDto(item.Id, item.TripId, item.EntryType.ToString(), item.Amount,
            item.ReferenceType.ToString(), item.ReferenceId, item.CreatedAt)).ToList();
        return PagedResult<LedgerEntryDto>.Create(items, options.Page, options.PageSize, total);
    }

    public async Task<PagedResult<InvoiceListItemDto>> ListInvoicesAsync(
        Guid operatorId, PageOptions options, string? status, CancellationToken ct)
    {
        ValidatePage(options, ["issuedAt", "createdAt", "amount", "invoiceNumber"]);
        var query = _db.Invoices.AsNoTracking().Where(item => item.OperatorId == operatorId);
        if (ParseOptional<InvoiceStatus>(status) is { } parsedStatus)
            query = query.Where(item => item.Status == parsedStatus);
        query = ApplyDates(query, options);
        var total = await query.LongCountAsync(ct);
        query = (options.SortBy ?? "createdAt", IsAscending(options)) switch
        {
            ("issuedAt", true) => query.OrderBy(item => item.IssuedAt),
            ("issuedAt", false) => query.OrderByDescending(item => item.IssuedAt),
            ("amount", true) => query.OrderBy(item => item.Amount),
            ("amount", false) => query.OrderByDescending(item => item.Amount),
            ("invoiceNumber", true) => query.OrderBy(item => item.InvoiceNumber),
            ("invoiceNumber", false) => query.OrderByDescending(item => item.InvoiceNumber),
            ("createdAt", true) => query.OrderBy(item => item.CreatedAt),
            _ => query.OrderByDescending(item => item.CreatedAt),
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
        PageOptions options, Guid? operatorId, string? status, Guid? tripId, bool stuckOnly, string? severity, CancellationToken ct)
    {
        var page = await LoadSettlementRowsAsync(options, operatorId, status, tripId, stuckOnly, severity, ct);
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
        PageOptions options, string? type, string? referenceType, CancellationToken ct)
    {
        ValidatePage(options, ["createdAt", "amount"]);
        var query = _db.PlatformWalletTransactions.AsNoTracking().AsQueryable();
        if (ParseOptional<PlatformWalletTransactionType>(type) is { } parsedType)
            query = query.Where(item => item.Type == parsedType);
        if (ParseOptional<PlatformWalletTransactionRef>(referenceType) is { } parsedReference)
            query = query.Where(item => item.ReferenceType == parsedReference);
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
        PageOptions options, Guid? operatorId, string? status, Guid? tripId, bool stuckOnly, string? severity, CancellationToken ct)
    {
        var page = await LoadSettlementRowsAsync(options, operatorId, status, tripId, stuckOnly, severity, ct);
        var items = page.Rows.Select(item => new SettlementDto(item.Id, item.TripId,
            item.Status.ToString(), item.EligibleAt, item.NetAmount, item.SettlementMethod?.ToString(), item.SettledAt,
            item.CreatedAt)).ToList();
        return PagedResult<SettlementDto>.Create(items, options.Page, options.PageSize, page.Total);
    }

    private async Task<SettlementPageRows> LoadSettlementRowsAsync(
        PageOptions options, Guid? operatorId, string? status, Guid? tripId, bool stuckOnly, string? severity, CancellationToken ct)
    {
        ValidatePage(options, ["createdAt", "eligibleAt", "settledAt", "netAmount"]);
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
        query = ApplyDates(query, options);
        var total = await query.LongCountAsync(ct);
        query = (options.SortBy ?? "createdAt", IsAscending(options)) switch
        {
            ("eligibleAt", true) => query.OrderBy(item => item.EligibleAt),
            ("eligibleAt", false) => query.OrderByDescending(item => item.EligibleAt),
            ("settledAt", true) => query.OrderBy(item => item.SettledAt),
            ("settledAt", false) => query.OrderByDescending(item => item.SettledAt),
            ("netAmount", true) => query.OrderBy(item => item.NetAmount),
            ("netAmount", false) => query.OrderByDescending(item => item.NetAmount),
            ("createdAt", true) => query.OrderBy(item => item.CreatedAt),
            _ => query.OrderByDescending(item => item.CreatedAt),
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
        var type = Enum.Parse<OperatorWalletTransactionType>(request.Type, false);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
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
                movement.Id, movement.Id, request.Note), ct);
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

    private sealed record SettlementPageRows(IReadOnlyList<OperatorTripSettlement> Rows, long Total);

    private static IQueryable<T> ApplyDates<T>(IQueryable<T> query, PageOptions options) where T : BaseEntity<Guid>
    {
        if (options.From.HasValue)
            query = query.Where(item => item.CreatedAt >= options.From.Value);
        if (options.To.HasValue)
            query = query.Where(item => item.CreatedAt <= options.To.Value);
        return query;
    }
}
