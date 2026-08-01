namespace VietRide.Parcel.UnitTests.Infrastructure;

public sealed class ExceptionMessageHandler : HttpMessageHandler
{
    private readonly Exception _exception;

    public ExceptionMessageHandler(Exception exception)
    {
        _exception = exception;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<HttpResponseMessage>(cancellationToken)
            : Task.FromException<HttpResponseMessage>(_exception);
}
