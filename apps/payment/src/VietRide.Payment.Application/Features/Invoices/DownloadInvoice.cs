using MediatR;
using VietRide.Payment.Application.Abstractions.Services;

namespace VietRide.Payment.Application.Features.Invoices;

public sealed record DownloadInvoiceQuery(Guid InvoiceId, Guid OperatorId, Guid UserId)
    : IRequest<InvoiceDownloadUrl>;

public sealed class DownloadInvoiceQueryHandler
    : IRequestHandler<DownloadInvoiceQuery, InvoiceDownloadUrl>
{
    private readonly IInvoiceLifecycleService _service;

    public DownloadInvoiceQueryHandler(IInvoiceLifecycleService service) => _service = service;

    public Task<InvoiceDownloadUrl> Handle(
        DownloadInvoiceQuery request,
        CancellationToken cancellationToken)
        => _service.CreateDownloadAsync(
            request.InvoiceId,
            request.OperatorId,
            request.UserId,
            cancellationToken);
}
