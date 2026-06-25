namespace VietRide.Shared.Application.Exceptions;

public interface ICodedHttpException
{
    int StatusCode { get; }

    string ErrorCode { get; }

    string Message { get; }
}
