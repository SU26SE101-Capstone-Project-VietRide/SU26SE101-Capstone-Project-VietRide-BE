using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Exceptions;

public sealed class FirebaseDependencyUnavailableException : Exception, ICodedHttpException
{
    public int StatusCode => 502;

    public string ErrorCode => "UPSTREAM_UNAVAILABLE";

    public FirebaseDependencyUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
