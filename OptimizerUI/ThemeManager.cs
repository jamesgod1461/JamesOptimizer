using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace JamesOptimizer;

/// <summary>
/// Persisted user preferences for James Optimizer.
/// Stored as JSON at %LocalAppData%\JamesOptimizer\settings.json.
/// </summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "dark";          // dark | light
    public string Accent { get; set; } = "cyan";         // cyan | violet | emerald | sunset | rose
    public string Backdrop { get; set; } = "acrylic";    // acrylic | mica | none
    public bool AnimationsEnabled { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool CloseToTray { get; set; } = false;
    public bool WalkthroughSeen { get; set; } = false;
}

public sealed record AccentOption(string Id, string Name, System.Windows.Media.Color Primary, System.Windows.Media.Color Secondary);

/// <summary>
/// Central theme engine: swaps the color resource dictionary (dark/light),
/// layers accent overlay dictionaries on top, applies the DWM backdrop,
/// and persists all choices to disk. Initialize() must run before the
/// MainWindow is shown so the saved theme applies without a flash.
/// </summary>
public static class ThemeManager
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JamesOptimizer");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Settings { get; private set; } = new();

    public static bool IsDarkTheme => !string.Equals(Settings.Theme, "light", StringComparison.OrdinalIgnoreCase);
    public static bool AnimationsEnabled => Settings.AnimationsEnabled;

    /// <summary>Raised after the color dictionary has been swapped and brushes re-resolved.</summary>
    public static event Action? ThemeChanged;

    public static readonly List<AccentOption> Accents = new()
    {
        new AccentOption("cyan",    "Cyber Cyan",    System.Windows.Media.Color.FromRgb(0x00, 0xC8, 0xF5), System.Windows.Media.Color.FromRgb(0x6E, 0x7B, 0xFF)),
        new AccentOption("violet",  "Violet Nebula", System.Windows.Media.Color.FromRgb(0x8B, 0x5C, 0xF6), System.Windows.Media.Color.FromRgb(0xC0, 0x84, 0xFC)),
        new AccentOption("emerald", "Emerald Rush",  System.Windows.Media.Color.FromRgb(0x10, 0xC9, 0x8F), System.Windows.Media.Color.FromRgb(0x2D, 0xD4, 0xBF)),
        new AccentOption("sunset",  "Solar Flare",   System.Windows.Media.Color.FromRgb(0xFB, 0x7C, 0x25), System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24)),
        new AccentOption("rose",    "Cosmic Rose",   System.Windows.Media.Color.FromRgb(0xF4, 0x3F, 0x5E), System.Windows.Media.Color.FromRgb(0xFB, 0x71, 0x85)),
    };

    private static ResourceDictionary? _accentOverlay;

    // ═════════════════════════════════════════════════════════════════
    // INITIALIZATION & PERSISTENCE
    // ═════════════════════════════════════════════════════════════════
    public static void Initialize()
    {
        Settings = LoadSettings();
        ApplyColorDictionary();
        ApplyAccentOverlay();
    }

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (loaded != null) return loaded;
            }
        }
        catch { /* corrupt settings fall back to defaults */ }
        return new AppSettings();
    }

    public static void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch { /* saving is best-effort */ }
    }

    // ═════════════════════════════════════════════════════════════════
    // THEME (DARK / LIGHT)
    // ═════════════════════════════════════════════════════════════════
    public static void SetTheme(bool isDark, bool persist = true)
    {
        Settings.Theme = isDark ? "dark" : "light";
        ApplyColorDictionary();
        ApplyAccentOverlay();
        MainWindow.Instance?.ApplyWindowChromeTheme();
        if (persist) SaveSettings();
        ThemeChanged?.Invoke();
    }

    private static void ApplyColorDictionary()
    {
        var app = Application.Current;
        if (app == null) return;

        var source = IsDarkTheme ? "Themes/ThemeColors.xaml" : "Themes/Colors.Light.xaml";

        // Replace the existing color dictionary in place (identified by its
        // sentinel key) so its merge position — and therefore lookup order
        // for StaticResource inside sibling dictionaries — is preserved.
        var dicts = app.Resources.MergedDictionaries;
        for (int i = 0; i < dicts.Count; i++)
        {
            if (dicts[i].Contains("ColorAppBackground"))
            {
                dicts[i] = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
                return;
            }
        }

        // Fallback: no color dictionary found yet, insert at the front.
        dicts.Insert(0, new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
    }

    // ═════════════════════════════════════════════════════════════════
    // ACCENT OVERLAYS
    // ═════════════════════════════════════════════════════════════════
    public static AccentOption? GetAccent(string? id) =>
        Accents.Find(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    public static void SetAccent(string accentId, bool persist = true)
    {
        Settings.Accent = GetAccent(accentId)?.Id ?? "cyan";
        ApplyAccentOverlay();
        if (persist) SaveSettings();
        ThemeChanged?.Invoke();
    }

    private static void ApplyAccentOverlay()
    {
        var app = Application.Current;
        if (app == null) return;

        var dicts = app.Resources.MergedDictionaries;

        // "cyan" is the built-in default: no overlay, theme dictionaries win.
        if (_accentOverlay != null)
        {
            dicts.Remove(_accentOverlay);
            _accentOverlay = null;
        }

        if (string.Equals(Settings.Accent, "cyan", StringComparison.OrdinalIgnoreCase))
            return;

        var source = $"Themes/Accents/Accent.{Settings.Accent}.xaml";
        _accentOverlay = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };

        // Appending last gives the overlay the highest lookup precedence.
        dicts.Add(_accentOverlay);
    }

    // ═════════════════════════════════════════════════════════════════
    // BACKDROP (ACRYLIC / MICA / NONE)
    // ═════════════════════════════════════════════════════════════════
    public static void SetBackdrop(string backdrop, bool persist = true)
    {
        Settings.Backdrop = backdrop;
        MainWindow.Instance?.ApplyWindowChromeTheme();
        if (persist) SaveSettings();
    }

    /// <summary>
    /// Applies the DWM backdrop and window tint for the current theme.
    /// Called by MainWindow on load and whenever theme/backdrop changes.
    /// </summary>
    public static void ApplyWindowBackdrop(System.Windows.Controls.Border rootBorder)
    {
        var window = MainWindow.Instance;
        if (window == null) return;

        bool success = false;
        switch (Settings.Backdrop)
        {
            case "mica":
                WindowBackdropHelper.EnableMica(window);
                success = true;
                break;
            case "none":
                WindowBackdropHelper.DisableBackdrop(window);
                break;
            default: // acrylic
                success = WindowBackdropHelper.TryEnableAcrylic(window, IsDarkTheme);
                break;
        }

        rootBorder.Background = success
            ? (System.Windows.Media.Brush)Application.Current.FindResource("AcrylicTint")
            : (System.Windows.Media.Brush)Application.Current.FindResource("AppBackground");
    }

    // ═════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════
    /// <summary>Accent primary color for code-built visuals; falls back to cyan.</summary>
    public static System.Windows.Media.Color GetAccentColor()
    {
        if (Application.Current?.TryFindResource("ColorAccentCyan") is System.Windows.Media.Color c)
            return c;
        return System.Windows.Media.Color.FromRgb(0x00, 0xE5, 0xFF);
    }

    public static System.Windows.Media.Brush? GetBrush(string key) =>
        Application.Current?.TryFindResource(key) as System.Windows.Media.Brush;
}
