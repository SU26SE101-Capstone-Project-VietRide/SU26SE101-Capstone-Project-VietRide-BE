using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;

namespace VietRide.Payment.Infrastructure;

/// Payment service EF Core context — owns schema `vietride_payment`.
public sealed class PaymentDbContext : VietRideDbContextBase, IBatchChargePaymentDbContext
{
    public const string SchemaName = "vietride_payment";

    public PaymentDbContext(DbContextOptions<PaymentDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    public static void ConfigurePostgresTypes(NpgsqlDataSourceBuilder dataSourceBuilder)
    {
        var translator = new NpgsqlNullNameTranslator();
        dataSourceBuilder.MapEnum<PaymentReferenceType>("payment_reference_type", translator);
        dataSourceBuilder.MapEnum<PaymentMethod>("payment_method", translator);
        dataSourceBuilder.MapEnum<PaymentStatus>("payment_status", translator);
        dataSourceBuilder.MapEnum<WalletTransactionType>("wallet_transaction_type", translator);
        dataSourceBuilder.MapEnum<WalletTransactionReferenceType>("wallet_transaction_ref", translator);
    }

    public DbSet<Payment.Domain.Entities.Payment> Payments => Set<Payment.Domain.Entities.Payment>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();

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

    public void AddPayment(Payment.Domain.Entities.Payment payment)
        => Payments.Add(payment);

    public void AddWalletTransaction(WalletTransaction transaction)
        => WalletTransactions.Add(transaction);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
