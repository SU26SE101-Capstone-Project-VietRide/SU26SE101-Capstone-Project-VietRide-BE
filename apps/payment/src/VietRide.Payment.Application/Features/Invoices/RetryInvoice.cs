using MediatR;

namespace VietRide.Payment.Application.Features.Invoices;

public sealed record RetryInvoiceCommand(Guid InvoiceId) : IRequest<RetryInvoiceResult>;

public sealed record RetryInvoiceResult(
    Guid InvoiceId,
    string PdfGenerationStatus,
    int AttemptsUsed);

public sealed class RetryInvoiceCommandHandler
    : IRequestHandler<RetryInvoiceCommand, RetryInvoiceResult>
{
    private readonly IInvoiceLifecycleService _service;

    public RetryInvoiceCommandHandler(IInvoiceLifecycleService service) => _service = service;

    public Task<RetryInvoiceResult> Handle(
        RetryInvoiceCommand request,
        CancellationToken cancellationToken)
        => _service.RetryAsync(request.InvoiceId, cancellationToken);
}
