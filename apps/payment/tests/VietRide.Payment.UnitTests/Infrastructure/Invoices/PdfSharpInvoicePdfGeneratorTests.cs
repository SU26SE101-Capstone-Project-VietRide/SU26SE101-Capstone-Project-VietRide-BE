using FluentAssertions;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Infrastructure.Invoices;

namespace VietRide.Payment.UnitTests.Infrastructure.Invoices;

public sealed class PdfSharpInvoicePdfGeneratorTests
{
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
                "Quận 1",
                "Thành phố Hồ Chí Minh"));

        var bytes = await generator.GenerateAsync(document, CancellationToken.None);

        bytes.Should().HaveCountGreaterThan(10_000);
        bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8).Should().BeTrue();
        using var stream = new MemoryStream(bytes);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        pdf.PageCount.Should().Be(1);
    }
}
