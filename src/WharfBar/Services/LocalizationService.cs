using System.Globalization;
using System.Resources;

namespace WharfBar.Services;

/// <summary>
/// UI language normalize / culture resolve / resource lookup.
/// </summary>
internal static class LocalizationService
{
    private static readonly ResourceManager Resources =
        new("WharfBar.Resources.Strings", typeof(LocalizationService).Assembly);

    internal const string LanguageSystem = "system";
    internal const string LanguageEn = "en";
    internal const string LanguageTr = "tr";

    internal static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return LanguageSystem;
        }

        if (string.Equals(language.Trim(), LanguageEn, StringComparison.OrdinalIgnoreCase))
        {
            return LanguageEn;
        }

        if (string.Equals(language.Trim(), LanguageTr, StringComparison.OrdinalIgnoreCase))
        {
            return LanguageTr;
        }

        if (string.Equals(language.Trim(), LanguageSystem, StringComparison.OrdinalIgnoreCase))
        {
            return LanguageSystem;
        }

        return LanguageSystem;
    }

    /// <summary>
    /// Resolves the culture for <paramref name="language"/>.
    /// For <c>system</c>, uses <paramref name="systemUiCulture"/> when provided;
    /// otherwise <see cref="CultureInfo.CurrentUICulture"/>.
    /// </summary>
    internal static CultureInfo ResolveCulture(string language, CultureInfo? systemUiCulture = null)
    {
        string normalized = NormalizeLanguage(language);
        if (normalized == LanguageEn)
        {
            return CultureInfo.GetCultureInfo("en");
        }

        if (normalized == LanguageTr)
        {
            return CultureInfo.GetCultureInfo("tr-TR");
        }

        CultureInfo ui = systemUiCulture ?? CultureInfo.CurrentUICulture;
        if (ui.Name.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
            || ui.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo("tr-TR");
        }

        return CultureInfo.GetCultureInfo("en");
    }

    internal static void Apply(string language)
    {
        CultureInfo culture = ResolveCulture(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    internal static string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return Resources.GetString(key) ?? key;
    }
}
