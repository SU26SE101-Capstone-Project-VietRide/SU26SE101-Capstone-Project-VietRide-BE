using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.OperatorReports;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.UnitTests.Features.OperatorReports;

public sealed class ExportBookingReportQueryHandlerTests
{
    private static readonly Guid OperatorId = Guid.Parse("41000000-0000-4000-8000-000000000001");
    private static readonly Guid BookingId = Guid.Parse("41000000-0000-4000-8000-000000000002");
    private static readonly Guid TripId = Guid.Parse("41000000-0000-4000-8000-000000000003");

    [Theory]
    [InlineData(BookingOperatorReportKind.Bookings, "Đặt vé", "bao-cao-dat-ve-20260718-20260718.xlsx", false)]
    [InlineData(BookingOperatorReportKind.Cancellations, "Hủy vé", "bao-cao-huy-ve-20260718-20260718.xlsx", true)]
    public async Task Handle_UsesTenantRangeAndStableWorkbookContract(
        BookingOperatorReportKind kind,
        string sheet,
        string fileName,
        bool cancellationOnly)
    {
        var repository = Substitute.For<IBookingRepository>();
        repository.StreamOperatorReportRowsAsync(
                OperatorId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                cancellationOnly,
                Arg.Any<CancellationToken>())
            .Returns(Rows(new BookingOperatorReportRow(
                BookingId,
                "VR-REPORT",
                TripId,
                "TP.HCM - Đà Lạt",
                "Bến xe Miền Đông",
                "Bến xe Đà Lạt",
                cancellationOnly ? BookingStatus.CANCELLED : BookingStatus.COMPLETED,
                2,
                200_000,
                new DateTimeOffset(2026, 7, 18, 1, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 18, 1, 5, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 18, 2, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 18, 1, 30, 0, TimeSpan.Zero),
                BookingCancellationReason.USER_INITIATED)));
        var writer = new CapturingWriter();
        var handler = new ExportBookingReportQueryHandler(
            repository,
            writer,
            new FixedClock());

        await using var report = await handler.Handle(
            new ExportBookingReportQuery(
                OperatorId,
                new DateOnly(2026, 7, 18),
                new DateOnly(2026, 7, 18),
                kind),
            CancellationToken.None);

        writer.Spec!.SheetName.Should().Be(sheet);
        writer.Spec.FileName.Should().Be(fileName);
        writer.Rows.Should().ContainSingle();
        writer.Rows[0].Cells[0].Text.Should().Be("VR-REPORT");
        writer.Rows[0].Cells[4].Text.Should().Be(cancellationOnly ? "Đã hủy" : "Hoàn thành");
        writer.Rows[0].Cells[^2].Text.Should().Be(BookingId.ToString("D"));
        repository.Received(1).StreamOperatorReportRowsAsync(
            OperatorId,
            new DateTimeOffset(2026, 7, 17, 17, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 18, 17, 0, 0, TimeSpan.Zero),
            cancellationOnly,
            Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<BookingOperatorReportRow> Rows(
        BookingOperatorReportRow row)
    {
        yield return row;
        await Task.Yield();
    }

    private sealed class CapturingWriter : IExcelReportWriter
    {
        public ExcelReportSpec? Spec { get; private set; }
        public List<ExcelReportRow> Rows { get; } = [];

        public async Task<ExcelReportStream> WriteAsync(
            ExcelReportSpec spec,
            IAsyncEnumerable<ExcelReportRow> rows,
            CancellationToken cancellationToken = default)
        {
            Spec = spec;
            await foreach (var row in rows.WithCancellation(cancellationToken)) Rows.Add(row);
            return new ExcelReportStream(new MemoryStream(), spec.FileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    }
}
