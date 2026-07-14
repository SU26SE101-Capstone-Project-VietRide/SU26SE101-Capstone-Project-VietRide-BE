using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Exceptions;

public sealed class BookingPaymentException : Exception, ICodedHttpException
{
    public int StatusCode { get; }
    public string ErrorCode { get; }

    public BookingPaymentException(int statusCode, string errorCode, string message) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}
