using FluentAssertions;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Infrastructure.Invoices;

namespace VietRide.Payment.UnitTests.Infrastructure.Invoices;

public sealed class PdfSharpInvoicePdfGeneratorTests
{
    [Theory]
    [InlineData("MONTHLY", "Hàng tháng")]
    [InlineData("YEARLY", "Hàng năm")]
    [InlineData("UNKNOWN", "Không xác định")]
    public void DisplayLabels_UseVietnameseContract(string period, string expected)
    {
        InvoicePdfDisplayLabels.BillingPeriod(period).Should().Be(expected);
        InvoicePdfDisplayLabels.Amount(500_000).Should().EndWith("VNĐ");
        InvoicePdfDisplayLabels.IssuedAt(new DateTimeOffset(2026, 7, 14, 1, 0, 0, TimeSpan.Zero))
            .Should().Be("14/07/2026 08:00 (giờ Việt Nam)");
    }

    [Fact]
    public void FileMetadata_UsesFriendlyVietnameseDownloadName()
    {
        var fileName = InvoicePdfFileMetadata.DownloadFileName("VR-INV-202607-000001");
        fileName.Should().Be("hoa-don-VR-INV-202607-000001.pdf");
        InvoicePdfFileMetadata.ContentDisposition(fileName)
            .Should().Be("attachment; filename*=UTF-8''hoa-don-VR-INV-202607-000001.pdf");
    }

    [Fact]
    public async Task GenerateAsync_WithVietnameseSnapshot_ProducesNonEmptyReadablePdf()
    {
        var generator = new PdfSharpInvoicePdfGenerator(Options.Create(new InvoicePdfOptions
        {
            PublisherName = "VIETRIDE",
            PublisherTaxCode = "0312345678",
            PublisherAddress = "Thành phố Hồ Chí Minh, Việt Nam",
        }));
        var document = new InvoicePdfDocument(
            "VR-INV-202607-000001",
            new DateTimeOffset(2026, 7, 14, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero),
            "Gói Doanh nghiệp Việt",
            "MONTHLY",
            500_000,
            new InvoicePdfBuyer(
                "Nhà xe Ánh Dương",
                "ĐKKD-001",
                "0312345678",
                "billing@example.test",
                "0900000000",
                "123 Nguyễn Huệ",
                "Phường Bến Nghé",
                "Thành phố Hồ Chí Minh"));

        var bytes = await generator.GenerateAsync(document, CancellationToken.None);

        bytes.Should().HaveCountGreaterThan(10_000);
        bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8).Should().BeTrue();
        using var stream = new MemoryStream(bytes);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        pdf.PageCount.Should().Be(1);
    }
}
