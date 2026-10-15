using System.Globalization;
using System.Resources;
using WharfBar.Models;

namespace WharfBar.Services;

internal static class LocalizationService
{
    private static readonly ResourceManager Resources =
        new("WharfBar.Resources.Strings", typeof(LocalizationService).Assembly);

    internal static string NormalizeLanguage(string? value)
    {
        if (string.Equals(value, AppConfig.LanguageEn, StringComparison.OrdinalIgnoreCase))
        {
            return AppConfig.LanguageEn;
        }

        if (string.Equals(value, AppConfig.LanguageTr, StringComparison.OrdinalIgnoreCase))
        {
            return AppConfig.LanguageTr;
        }

        return AppConfig.LanguageSystem;
    }

    internal static CultureInfo ResolveCulture(string languageToken, CultureInfo? installedUiCulture = null)
    {
        string token = NormalizeLanguage(languageToken);
        if (token == AppConfig.LanguageEn)
        {
            return CultureInfo.GetCultureInfo("en-US");
        }

        if (token == AppConfig.LanguageTr)
        {
            return CultureInfo.GetCultureInfo("tr-TR");
        }

        CultureInfo os = installedUiCulture ?? CultureInfo.InstalledUICulture;
        if (os.Name.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
            || os.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo("tr-TR");
        }

        return CultureInfo.GetCultureInfo("en-US");
    }

    internal static void Apply(string languageToken)
    {
        CultureInfo culture = ResolveCulture(languageToken);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>Looks up a UI string; returns <paramref name="key"/> when missing.</summary>
    internal static string Get(string key) => Resources.GetString(key) ?? key;
}
