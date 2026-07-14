namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IInvoiceNumberCounterRepository
{
    Task<long> NextAsync(string periodKey, CancellationToken cancellationToken);
}
