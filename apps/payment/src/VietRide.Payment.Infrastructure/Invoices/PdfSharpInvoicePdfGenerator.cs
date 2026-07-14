using System.Globalization;
using Microsoft.Extensions.Options;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using VietRide.Payment.Application.Abstractions.Services;

namespace VietRide.Payment.Infrastructure.Invoices;

public sealed class PdfSharpInvoicePdfGenerator : IInvoicePdfGenerator
{
    private static readonly object FontResolverLock = new();
    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");
    private readonly InvoicePdfOptions _options;

    public PdfSharpInvoicePdfGenerator(IOptions<InvoicePdfOptions> options)
    {
        _options = options.Value;
        EnsureFontResolver();
    }

    public Task<byte[]> GenerateAsync(InvoicePdfDocument invoice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(invoice);

        var document = BuildDocument(invoice);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, closeStream: false);
        return Task.FromResult(output.ToArray());
    }

    private Document BuildDocument(InvoicePdfDocument invoice)
    {
        var document = new Document
        {
            Info =
            {
                Title = $"Hóa đơn {invoice.InvoiceNumber}",
                Author = _options.PublisherName,
                Subject = "Hóa đơn thanh toán gói dịch vụ VietRide",
            },
        };

        ConfigureStyles(document);
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.6);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.6);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);

        AddHeader(section, invoice);
        AddParties(section, invoice.Buyer);
        AddServiceDetails(section, invoice);
        AddTotals(section, invoice.AmountVnd);

        var vat = section.AddParagraph(_options.VatNote);
        vat.Format.SpaceBefore = Unit.FromCentimeter(0.5);
        vat.Format.Font.Size = 9;
        vat.Format.Font.Color = Colors.DimGray;

        var footer = section.Footers.Primary.AddParagraph();
        footer.AddText("Tài liệu được phát hành điện tử bởi VietRide.");
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = Colors.Gray;

        return document;
    }

    private static void ConfigureStyles(Document document)
    {
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = NotoSansFontResolver.FamilyName;
        normal.Font.Size = 10;

        var heading = document.Styles[StyleNames.Heading1]!;
        heading.Font.Name = NotoSansFontResolver.FamilyName;
        heading.Font.Bold = true;
        heading.Font.Size = 18;
        heading.ParagraphFormat.SpaceAfter = Unit.FromCentimeter(0.2);
    }

    private void AddHeader(Section section, InvoicePdfDocument invoice)
    {
        var publisher = section.AddParagraph(_options.PublisherName);
        publisher.Format.Font.Bold = true;
        publisher.Format.Font.Size = 12;

        if (!string.IsNullOrWhiteSpace(_options.PublisherTaxCode))
            section.AddParagraph($"Mã số thuế: {_options.PublisherTaxCode}");
        if (!string.IsNullOrWhiteSpace(_options.PublisherAddress))
            section.AddParagraph($"Địa chỉ: {_options.PublisherAddress}");

        var title = section.AddParagraph("HÓA ĐƠN DỊCH VỤ", StyleNames.Heading1);
        title.Format.Alignment = ParagraphAlignment.Center;
        title.Format.SpaceBefore = Unit.FromCentimeter(0.5);

        var number = section.AddParagraph($"Số hóa đơn: {invoice.InvoiceNumber}");
        number.Format.Alignment = ParagraphAlignment.Center;
        number.Format.Font.Bold = true;

        var issuedAt = section.AddParagraph($"Ngày phát hành: {invoice.IssuedAt:dd/MM/yyyy HH:mm} (UTC)");
        issuedAt.Format.Alignment = ParagraphAlignment.Center;
        issuedAt.Format.SpaceAfter = Unit.FromCentimeter(0.7);
    }

    private void AddParties(Section section, InvoicePdfBuyer buyer)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.AddColumn(Unit.FromCentimeter(4.6));
        table.AddColumn(Unit.FromCentimeter(11.2));

        AddRow(table, "Đơn vị mua hàng", buyer.Name, boldValue: true);
        AddRow(table, "Mã số doanh nghiệp", buyer.BusinessRegistrationNumber);
        AddRow(table, "Mã số thuế", buyer.TaxCode);
        AddRow(table, "Địa chỉ", FormatAddress(buyer));
        AddRow(table, "Liên hệ", $"{buyer.ContactEmail} | {buyer.ContactPhone}");
    }

    private static void AddServiceDetails(Section section, InvoicePdfDocument invoice)
    {
        var heading = section.AddParagraph("Chi tiết dịch vụ");
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 12;
        heading.Format.SpaceBefore = Unit.FromCentimeter(0.7);
        heading.Format.SpaceAfter = Unit.FromCentimeter(0.2);

        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.AddColumn(Unit.FromCentimeter(7.4));
        table.AddColumn(Unit.FromCentimeter(4.6));
        table.AddColumn(Unit.FromCentimeter(3.8));

        var header = table.AddRow();
        header.Shading.Color = Colors.LightGray;
        header.Format.Font.Bold = true;
        header.Cells[0].AddParagraph("Gói dịch vụ");
        header.Cells[1].AddParagraph("Kỳ sử dụng");
        header.Cells[2].AddParagraph("Thành tiền");

        var row = table.AddRow();
        row.Cells[0].AddParagraph($"{invoice.PlanName} ({FormatBillingPeriod(invoice.BillingPeriod)})");
        row.Cells[1].AddParagraph($"{invoice.PeriodFrom:dd/MM/yyyy} - {invoice.PeriodTo:dd/MM/yyyy}");
        var amount = row.Cells[2].AddParagraph(FormatVnd(invoice.AmountVnd));
        amount.Format.Alignment = ParagraphAlignment.Right;
    }

    private static void AddTotals(Section section, long amountVnd)
    {
        var total = section.AddParagraph();
        total.Format.Alignment = ParagraphAlignment.Right;
        total.Format.Font.Bold = true;
        total.Format.Font.Size = 12;
        total.Format.SpaceBefore = Unit.FromCentimeter(0.5);
        total.AddText($"TỔNG CỘNG: {FormatVnd(amountVnd)}");
    }

    private static void AddRow(Table table, string label, string value, bool boldValue = false)
    {
        var row = table.AddRow();
        row.Cells[0].AddParagraph(label).Format.Font.Bold = true;
        var paragraph = row.Cells[1].AddParagraph(value);
        paragraph.Format.Font.Bold = boldValue;
    }

    private static string FormatAddress(InvoicePdfBuyer buyer)
    {
        var components = new[]
        {
            buyer.AddressStreet,
            buyer.AddressWard,
            buyer.AddressDistrict,
            buyer.AddressProvince,
        };
        var address = string.Join(", ", components.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(address) ? "Không cung cấp" : address;
    }

    private static string FormatBillingPeriod(string billingPeriod)
        => billingPeriod switch
        {
            "MONTHLY" => "Hàng tháng",
            "YEARLY" => "Hàng năm",
            _ => billingPeriod,
        };

    private static string FormatVnd(long amountVnd)
        => string.Format(VietnameseCulture, "{0:N0} VND", amountVnd);

    private static void Validate(InvoicePdfDocument invoice)
    {
        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber)
            || string.IsNullOrWhiteSpace(invoice.PlanName)
            || invoice.PeriodTo <= invoice.PeriodFrom
            || invoice.AmountVnd < 0)
        {
            throw new ArgumentException("Invoice PDF data is invalid.", nameof(invoice));
        }

        if (string.IsNullOrWhiteSpace(invoice.Buyer.Name)
            || string.IsNullOrWhiteSpace(invoice.Buyer.BusinessRegistrationNumber)
            || string.IsNullOrWhiteSpace(invoice.Buyer.TaxCode)
            || string.IsNullOrWhiteSpace(invoice.Buyer.ContactEmail)
            || string.IsNullOrWhiteSpace(invoice.Buyer.ContactPhone))
        {
            throw new ArgumentException("Invoice buyer snapshot is incomplete.", nameof(invoice));
        }
    }

    private static void EnsureFontResolver()
    {
        lock (FontResolverLock)
        {
            GlobalFontSettings.FontResolver ??= new NotoSansFontResolver();
        }
    }
}
