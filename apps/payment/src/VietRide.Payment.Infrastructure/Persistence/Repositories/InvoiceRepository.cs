using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly PaymentDbContext _db;
    public InvoiceRepository(PaymentDbContext db) => _db = db;
    public Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct) => _db.Invoices.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<Invoice> AddAsync(Invoice entity, CancellationToken ct) { await _db.Invoices.AddAsync(entity, ct); return entity; }
    public void Update(Invoice entity) => _db.Invoices.Update(entity);
    public void Remove(Invoice entity) => _db.Invoices.Remove(entity);
    public IQueryable<Invoice> Query() => _db.Invoices;
    public IQueryable<Invoice> QueryNoTracking() => _db.Invoices.AsNoTracking();
    public Task<Invoice?> FindByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken)
        => _db.Invoices.FirstOrDefaultAsync(x => x.PaymentId == paymentId, cancellationToken);
}
