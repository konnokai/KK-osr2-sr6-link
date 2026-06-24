using System;
using System.Linq;
using System.Windows;

namespace KKOsr2Sr6Link.Wpf.Localization;

/// <summary>
/// Runtime UI language switch. Keeps one string ResourceDictionary merged into the app
/// resources and swaps it on demand; XAML uses {DynamicResource L.*} so visible text
/// updates live. Code-set strings (toggle button states) read via <see cref="T"/>.
/// </summary>
public static class Loc
{
    public static readonly string[] Languages = { "en", "zh-Hant" };

    private static ResourceDictionary? _current;

    /// <summary>Merge the dictionary for <paramref name="lang"/>, replacing any prior one.</summary>
    public static void SetLanguage(string lang)
    {
        if (!Languages.Contains(lang)) lang = "en";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"/Localization/Strings.{lang}.xaml", UriKind.Relative)
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current != null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
    }

    /// <summary>Look up a localized string by key; falls back to the key itself.</summary>
    public static string T(string key) => Application.Current.TryFindResource(key) as string ?? key;

    /// <summary>Look up a localized format string and fill in <paramref name="args"/>.</summary>
    public static string T(string key, params object[] args) => string.Format(T(key), args);
}
