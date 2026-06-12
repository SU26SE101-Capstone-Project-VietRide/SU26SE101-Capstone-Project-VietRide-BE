using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.Infrastructure;

/// Payment service EF Core context — owns schema `vietride_payment`.
public sealed class PaymentDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_payment";

    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();
    public DbSet<TopUpRequest> TopUpRequests => Set<TopUpRequest>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<PlatformWallet> PlatformWallets => Set<PlatformWallet>();
    public DbSet<PlatformWalletTransaction> PlatformWalletTransactions => Set<PlatformWalletTransaction>();

    public PaymentDbContext(DbContextOptions<PaymentDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

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

    public static void ConfigurePostgresTypes(NpgsqlDataSourceBuilder dataSourceBuilder)
    {
        dataSourceBuilder.MapEnum<PaymentReferenceType>("payment_reference_type", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<PaymentMethod>("payment_method", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<PaymentStatus>("payment_status", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<TopUpRequestStatus>("top_up_request_status", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<WalletTransactionType>("wallet_transaction_type", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<WalletTransactionRef>("wallet_transaction_ref", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<PlatformWalletTransactionType>("platform_wallet_transaction_type", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<PlatformWalletTransactionRef>("platform_wallet_transaction_ref", new NpgsqlNullNameTranslator());
    }
}
