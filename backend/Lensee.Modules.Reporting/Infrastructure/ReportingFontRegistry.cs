using System.Reflection;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace Lensee.Modules.Reporting.Infrastructure;

internal static class ReportingFontRegistry
{
    private static int _registered;

    public static void RegisterBundledFonts()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;

        var assembly = typeof(ReportingFontRegistry).Assembly;
        Register(assembly, "NotoSansArabic-Variable.ttf");
        Register(assembly, "NotoSans-Variable.ttf");
    }

    private static void Register(Assembly assembly, string suffix)
    {
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Bundled reporting font '{suffix}' could not be loaded.");
        FontManager.RegisterFont(stream);
    }
}
