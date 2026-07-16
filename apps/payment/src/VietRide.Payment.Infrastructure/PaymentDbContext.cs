using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.Infrastructure;

/// Payment service EF Core context - owns schema `vietride_payment`.
public sealed class PaymentDbContext : VietRideDbContextBase, IBatchChargePaymentDbContext
{
    public const string SchemaName = "vietride_payment";

    public PaymentDbContext(DbContextOptions<PaymentDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();
    public DbSet<TopUpRequest> TopUpRequests => Set<TopUpRequest>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<PlatformWallet> PlatformWallets => Set<PlatformWallet>();
    public DbSet<PlatformWalletTransaction> PlatformWalletTransactions => Set<PlatformWalletTransaction>();
    public DbSet<RefundFailureLog> RefundFailureLogs => Set<RefundFailureLog>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceNumberCounter> InvoiceNumberCounters => Set<InvoiceNumberCounter>();
    public DbSet<OperatorWallet> OperatorWallets => Set<OperatorWallet>();
    public DbSet<OperatorWalletTransaction> OperatorWalletTransactions => Set<OperatorWalletTransaction>();
    public DbSet<OperatorLedgerEntry> OperatorLedgerEntries => Set<OperatorLedgerEntry>();
    public DbSet<OperatorTripSettlement> OperatorTripSettlements => Set<OperatorTripSettlement>();
    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<ProcessedIntegrationEvent>();

    public static void ConfigurePostgresTypes(NpgsqlDataSourceBuilder dataSourceBuilder)
    {
        var translator = new NpgsqlNullNameTranslator();
        dataSourceBuilder.MapEnum<PaymentReferenceType>($"{SchemaName}.payment_reference_type", translator);
        dataSourceBuilder.MapEnum<PaymentMethod>($"{SchemaName}.payment_method", translator);
        dataSourceBuilder.MapEnum<PaymentStatus>($"{SchemaName}.payment_status", translator);
        dataSourceBuilder.MapEnum<TopUpRequestStatus>($"{SchemaName}.top_up_request_status", translator);
        dataSourceBuilder.MapEnum<WalletTransactionType>($"{SchemaName}.wallet_transaction_type", translator);
        dataSourceBuilder.MapEnum<WalletTransactionRef>($"{SchemaName}.wallet_transaction_ref", translator);
        dataSourceBuilder.MapEnum<PlatformWalletTransactionType>($"{SchemaName}.platform_wallet_transaction_type", translator);
        dataSourceBuilder.MapEnum<PlatformWalletTransactionRef>($"{SchemaName}.platform_wallet_transaction_ref", translator);
        dataSourceBuilder.MapEnum<InvoiceStatus>($"{SchemaName}.invoice_status", translator);
        dataSourceBuilder.MapEnum<InvoicePdfGenerationStatus>($"{SchemaName}.invoice_pdf_generation_status", translator);
        dataSourceBuilder.MapEnum<OperatorWalletTransactionType>($"{SchemaName}.operator_wallet_transaction_type", translator);
        dataSourceBuilder.MapEnum<OperatorWalletTransactionRef>($"{SchemaName}.operator_wallet_transaction_ref", translator);
        dataSourceBuilder.MapEnum<OperatorLedgerEntryType>($"{SchemaName}.operator_ledger_entry_type", translator);
        dataSourceBuilder.MapEnum<OperatorLedgerReferenceType>($"{SchemaName}.operator_ledger_reference_type", translator);
        dataSourceBuilder.MapEnum<OperatorTripSettlementStatus>($"{SchemaName}.operator_trip_settlement_status", translator);
        dataSourceBuilder.MapEnum<OperatorTripSettlementMethod>($"{SchemaName}.operator_trip_settlement_method", translator);
    }

    public Task<Wallet?> FindWalletAsync(Guid userId, CancellationToken cancellationToken)
        => Wallets.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

    public async Task AcquirePaymentReferenceLocksAsync(
        IReadOnlyCollection<BatchChargePaymentCommand.Item> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items.OrderBy(x => x.ReferenceType, StringComparer.Ordinal).ThenBy(x => x.ReferenceId))
        {
            var lockKey = $"payment:{item.ReferenceType}:{item.ReferenceId:N}";
            await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtext({lockKey})::bigint)",
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task<bool> PaymentReferenceExistsAsync(string referenceType, Guid referenceId, CancellationToken cancellationToken)
        => Payments.AnyAsync(
            x => x.ReferenceType == Enum.Parse<PaymentReferenceType>(referenceType) && x.ReferenceId == referenceId,
            cancellationToken);

    public void AddPayment(PaymentEntity payment)
        => Payments.Add(payment);

    public void AddWalletTransaction(WalletTransaction transaction)
        => WalletTransactions.Add(transaction);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.HasPostgresEnum(SchemaName, "payment_reference_type", Enum.GetNames<PaymentReferenceType>());
        modelBuilder.HasPostgresEnum(SchemaName, "payment_method", Enum.GetNames<PaymentMethod>());
        modelBuilder.HasPostgresEnum(SchemaName, "payment_status", Enum.GetNames<PaymentStatus>());
        modelBuilder.HasPostgresEnum(SchemaName, "top_up_request_status", Enum.GetNames<TopUpRequestStatus>());
        modelBuilder.HasPostgresEnum(SchemaName, "wallet_transaction_type", Enum.GetNames<WalletTransactionType>());
        modelBuilder.HasPostgresEnum(SchemaName, "wallet_transaction_ref", Enum.GetNames<WalletTransactionRef>());
        modelBuilder.HasPostgresEnum(SchemaName, "platform_wallet_transaction_type", Enum.GetNames<PlatformWalletTransactionType>());
        modelBuilder.HasPostgresEnum(SchemaName, "platform_wallet_transaction_ref", Enum.GetNames<PlatformWalletTransactionRef>());
        modelBuilder.HasPostgresEnum(SchemaName, "invoice_status", Enum.GetNames<InvoiceStatus>());
        modelBuilder.HasPostgresEnum(SchemaName, "invoice_pdf_generation_status", Enum.GetNames<InvoicePdfGenerationStatus>());
        modelBuilder.HasPostgresEnum(SchemaName, "operator_wallet_transaction_type", Enum.GetNames<OperatorWalletTransactionType>());
        modelBuilder.HasPostgresEnum(SchemaName, "operator_wallet_transaction_ref", Enum.GetNames<OperatorWalletTransactionRef>());
        modelBuilder.HasPostgresEnum(SchemaName, "operator_ledger_entry_type", Enum.GetNames<OperatorLedgerEntryType>());
        modelBuilder.HasPostgresEnum(SchemaName, "operator_ledger_reference_type", Enum.GetNames<OperatorLedgerReferenceType>());
        modelBuilder.HasPostgresEnum(SchemaName, "operator_trip_settlement_status", Enum.GetNames<OperatorTripSettlementStatus>());
        modelBuilder.HasPostgresEnum(SchemaName, "operator_trip_settlement_method", Enum.GetNames<OperatorTripSettlementMethod>());

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
