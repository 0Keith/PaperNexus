using SixLabors.Fonts;

namespace PaperNexus.Core;

internal static class BundledFonts
{
    public const string DefaultFontFamily = "Cinzel";

    private static readonly Lazy<FontCollection> _collection = new(LoadBundledFonts);

    public static FontCollection Collection => _collection.Value;

    /// <summary>
    /// Bundled font names available regardless of system-installed fonts.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = [DefaultFontFamily];

    public static bool TryGet(string familyName, out FontFamily family)
    {
        if (Collection.TryGet(familyName, out family))
            return true;

        return SystemFonts.TryGet(familyName, out family);
    }

    private static FontCollection LoadBundledFonts()
    {
        var collection = new FontCollection();
        var assembly = typeof(BundledFonts).Assembly;
        using var stream = assembly.GetManifestResourceStream("PaperNexus.Cinzel.ttf");
        if (stream is not null)
            collection.Add(stream);
        return collection;
    }
}
