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

    public static void ConfigurePostgresTypes(NpgsqlDataSourceBuilder dataSourceBuilder)
    {
        var translator = new NpgsqlNullNameTranslator();
        dataSourceBuilder.MapEnum<PaymentReferenceType>("payment_reference_type", translator);
        dataSourceBuilder.MapEnum<PaymentMethod>("payment_method", translator);
        dataSourceBuilder.MapEnum<PaymentStatus>("payment_status", translator);
        dataSourceBuilder.MapEnum<TopUpRequestStatus>("top_up_request_status", translator);
        dataSourceBuilder.MapEnum<WalletTransactionType>("wallet_transaction_type", translator);
        dataSourceBuilder.MapEnum<WalletTransactionRef>("wallet_transaction_ref", translator);
        dataSourceBuilder.MapEnum<PlatformWalletTransactionType>("platform_wallet_transaction_type", translator);
        dataSourceBuilder.MapEnum<PlatformWalletTransactionRef>("platform_wallet_transaction_ref", translator);
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

        modelBuilder.HasPostgresEnum("payment_reference_type", Enum.GetNames<PaymentReferenceType>());
        modelBuilder.HasPostgresEnum("payment_method", Enum.GetNames<PaymentMethod>());
        modelBuilder.HasPostgresEnum("payment_status", Enum.GetNames<PaymentStatus>());
        modelBuilder.HasPostgresEnum("top_up_request_status", Enum.GetNames<TopUpRequestStatus>());
        modelBuilder.HasPostgresEnum("wallet_transaction_type", Enum.GetNames<WalletTransactionType>());
        modelBuilder.HasPostgresEnum("wallet_transaction_ref", Enum.GetNames<WalletTransactionRef>());
        modelBuilder.HasPostgresEnum("platform_wallet_transaction_type", Enum.GetNames<PlatformWalletTransactionType>());
        modelBuilder.HasPostgresEnum("platform_wallet_transaction_ref", Enum.GetNames<PlatformWalletTransactionRef>());

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
