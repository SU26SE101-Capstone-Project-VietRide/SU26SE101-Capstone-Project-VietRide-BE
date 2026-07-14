using PdfSharp.Fonts;

namespace VietRide.Payment.Infrastructure.Invoices;

internal sealed class NotoSansFontResolver : IFontResolver
{
    internal const string FamilyName = "Noto Sans VietRide";

    private const string RegularFace = "NotoSans-Regular";
    private const string BoldFace = "NotoSans-Bold";
    private const string ResourcePrefix = "VietRide.Payment.Infrastructure.Invoices.Fonts";

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        => new(isBold ? BoldFace : RegularFace, mustSimulateBold: false, mustSimulateItalic: isItalic);

    public byte[]? GetFont(string faceName)
        => faceName switch
        {
            RegularFace => ReadEmbeddedFont($"{ResourcePrefix}.NotoSans-Regular.ttf"),
            BoldFace => ReadEmbeddedFont($"{ResourcePrefix}.NotoSans-Bold.ttf"),
            _ => null,
        };

    private static byte[] ReadEmbeddedFont(string resourceName)
    {
        var assembly = typeof(NotoSansFontResolver).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Bundled invoice font '{resourceName}' was not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
