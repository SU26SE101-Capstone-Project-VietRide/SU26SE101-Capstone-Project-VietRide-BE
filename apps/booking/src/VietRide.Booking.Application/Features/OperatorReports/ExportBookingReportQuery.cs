using MediatR;
using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Application.Reporting;

namespace VietRide.Booking.Application.Features.OperatorReports;

public enum BookingOperatorReportKind
{
    Bookings,
    Cancellations,
}

public sealed record ExportBookingReportQuery(
    Guid OperatorId,
    DateOnly? From,
    DateOnly? To,
    BookingOperatorReportKind Kind) : IQuery<ExcelReportStream>;
