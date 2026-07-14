using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IInvoiceRepository : IRepository<Invoice, Guid>
{
    Task<Invoice?> FindByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken);
}
