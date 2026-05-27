using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;

namespace VietRide.Payment.Infrastructure;

/// Payment service EF Core context — owns schema `vietride_payment`.
public sealed class PaymentDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_payment";

    public PaymentDbContext(DbContextOptions<PaymentDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);
    }
}
