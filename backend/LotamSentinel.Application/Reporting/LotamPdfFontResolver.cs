using PdfSharp.Fonts;

namespace LotamSentinel.Application.Reporting;

/// <summary>
/// PDFsharp 6.x on .NET (non-.NET Framework) ships with NO font resolver at
/// all - unlike the old .NET Framework build, it cannot fall back to
/// whatever fonts happen to be installed on the machine (via GDI). Without
/// registering a resolver, PDF generation crashes on the very first font it
/// touches - even PDFsharp's own internal "predefined error font"
/// (hard-coded to request "Courier New"), which is why the crash mentions
/// Courier New even though nothing in our own document explicitly asks for
/// it.
///
/// Fix: route every font request to one of two genuinely free,
/// redistributable font families embedded directly in this assembly
/// (see Fonts/LICENSE-fonts.txt) - Liberation Sans (Arial-compatible) and
/// Liberation Mono (Courier New-compatible). This makes PDF export work
/// identically on every machine (a dev laptop, a server, CI) with zero
/// dependency on what fonts that machine happens to have installed.
/// </summary>
internal class LotamPdfFontResolver : IFontResolver
{
    private static readonly Dictionary<string, byte[]> FontCache = new();
    private static readonly object CacheLock = new();

    public byte[] GetFont(string faceName)
    {
        lock (CacheLock)
        {
            if (FontCache.TryGetValue(faceName, out var cached))
                return cached;

            var resourceName = $"LotamSentinel.Application.Reporting.Fonts.{faceName}.ttf";
            using var stream = typeof(LotamPdfFontResolver).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded font resource not found: {resourceName}. Check the EmbeddedResource " +
                    "entries in LotamSentinel.Application.csproj.");

            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            var bytes = memoryStream.ToArray();
            FontCache[faceName] = bytes;
            return bytes;
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Any family name containing these substrings gets the monospace
        // face - covers PDFsharp's internal "Courier New" request as well
        // as anything our own document code might ask for.
        var isMono = familyName.Contains("Courier", StringComparison.OrdinalIgnoreCase)
            || familyName.Contains("Mono", StringComparison.OrdinalIgnoreCase)
            || familyName.Contains("Consolas", StringComparison.OrdinalIgnoreCase);

        var faceName = (isMono, isBold, isItalic) switch
        {
            (true, true, true) => "LiberationMono-BoldItalic",
            (true, true, false) => "LiberationMono-Bold",
            (true, false, true) => "LiberationMono-Italic",
            (true, false, false) => "LiberationMono-Regular",
            (false, true, true) => "LiberationSans-BoldItalic",
            (false, true, false) => "LiberationSans-Bold",
            (false, false, true) => "LiberationSans-Italic",
            (false, false, false) => "LiberationSans-Regular",
        };

        return new FontResolverInfo(faceName);
    }
}
