using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using JamesOptimizer.WinUI.Theme;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace JamesOptimizer.WinUI.Views;

public class PerformanceTweakItem : INotifyPropertyChanged
{
    private bool _isEnabled;
    private bool _isApplicable = true;
    private bool _isDrifted;
    private string? _notApplicableReason;

    public TweakDefinition Definition { get; set; } = null!;
    public string Id => Definition.Id;
    public string Title => Definition.Title;
    public string Description => IsApplicable ? Definition.Description : $"{Definition.Description} [Unavailable: {_notApplicableReason}]";
    public string IconGlyph => Definition.IconGlyph;
    public string Category => Definition.Category;
    public string CategoryLabel => Definition.CategoryLabel;
    public string RegistryTarget => Definition.TargetDisplay;
    public string RiskText => Definition.RequiresAdmin ? $"{Definition.RiskLabel} · ADMIN" : Definition.RiskLabel;
    public string EvidenceText => Definition.EvidenceLabel;

    public bool IsApplicable
    {
        get => _isApplicable;
        set { _isApplicable = value; OnPropertyChanged(); OnPropertyChanged(nameof(CardOpacity)); OnPropertyChanged(nameof(Description)); OnPropertyChanged(nameof(StatusText)); }
    }

    public string? NotApplicableReason
    {
        get => _notApplicableReason;
        set { _notApplicableReason = value; OnPropertyChanged(); OnPropertyChanged(nameof(Description)); }
    }

    public bool IsDrifted
    {
        get => _isDrifted;
        set { _isDrifted = value; OnPropertyChanged(); OnPropertyChanged(nameof(DriftBadgeVisibility)); }
    }

    public Visibility DriftBadgeVisibility => _isDrifted ? Visibility.Visible : Visibility.Collapsed;
    public double CardOpacity => _isApplicable ? 1.0 : 0.55;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusDotBrush));
                OnPropertyChanged(nameof(CardBorderBrush));
            }
        }
    }

    public string StatusText => !IsApplicable ? "N/A" : _isEnabled ? "ACTIVE" : "STOCK";
    public SolidColorBrush StatusBrush => ThemeEffects.GetSolidBrush(_isEnabled ? "Accent.Cyan" : "Text.Secondary");
    public SolidColorBrush StatusDotBrush => ThemeEffects.GetSolidBrush(_isEnabled ? "State.Good" : "Text.Secondary");
    public SolidColorBrush CardBorderBrush => ThemeEffects.GetSolidBrush(_isEnabled ? "Border.Strong" : "Border.Subtle");

    public void RefreshThemeBrushes()
    {
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(StatusDotBrush));
        OnPropertyChanged(nameof(CardBorderBrush));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public sealed partial class PerformancePage : Page
{
    private readonly List<PerformanceTweakItem> _allTweaks = new();
    public ObservableCollection<PerformanceTweakItem> DisplayedTweaks { get; } = new();

    private string _currentCategory = "ALL";
    private string _searchFilter = "";
    private bool _suppressToggleEvents = false;
    private bool _themeHooked = false;

    public PerformancePage()
    {
        InitializeComponent();
        TweaksList.ItemsSource = DisplayedTweaks;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_themeHooked)
        {
            ActualThemeChanged += Page_ActualThemeChanged;
            _themeHooked = true;
        }

        if (_allTweaks.Count == 0)
        {
            foreach (var def in TweakCatalog.All)
                _allTweaks.Add(new PerformanceTweakItem { Definition = def });
            TxtCatalogCount.Text = $"{_allTweaks.Count} TWEAKS";
        }
        else
        {
            foreach (var item in _allTweaks)
                item.RefreshThemeBrushes();
        }

        await RefreshLiveStatesAsync();
        ApplyFilter();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_themeHooked)
        {
            ActualThemeChanged -= Page_ActualThemeChanged;
            _themeHooked = false;
        }
    }

    private void Page_ActualThemeChanged(FrameworkElement sender, object args)
    {
        foreach (var item in _allTweaks)
            item.RefreshThemeBrushes();
    }

    private async Task RefreshLiveStatesAsync()
    {
        _suppressToggleEvents = true;
        try
        {
            var results = await Task.Run(() =>
                _allTweaks.Select(t => (Item: t, State: t.Definition.Detect())).ToList());

            foreach (var (item, state) in results)
            {
                item.IsApplicable = state.IsApplicable;
                item.NotApplicableReason = state.NotApplicableReason;
                item.IsEnabled = state.IsApplied;
                item.IsDrifted = state.IsDrifted;
            }
            UpdateActiveCount();
        }
        finally
        {
            _suppressToggleEvents = false;
        }
    }

    private void ApplyFilter()
    {
        DisplayedTweaks.Clear();
        foreach (var tweak in _allTweaks)
        {
            bool matchesCategory = _currentCategory == "ALL" || tweak.Category.Equals(_currentCategory, StringComparison.OrdinalIgnoreCase);
            bool matchesSearch = string.IsNullOrWhiteSpace(_searchFilter) ||
                                 tweak.Title.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                                 tweak.Description.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                                 tweak.RegistryTarget.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                                 tweak.Definition.Tags.Any(tag => tag.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));

            if (matchesCategory && matchesSearch)
                DisplayedTweaks.Add(tweak);
        }
        UpdateActiveCount();
    }

    private void UpdateActiveCount()
    {
        int applicable = _allTweaks.Count(t => t.IsApplicable);
        int active = _allTweaks.Count(t => t.IsApplicable && t.IsEnabled);
        TxtActiveCount.Text = $"{active} / {applicable} Active";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchFilter = SearchBox.Text?.Trim() ?? "";
        ApplyFilter();
    }

    private void CategoryFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string cat) return;
        _currentCategory = cat;

        var activeStyle = (Style)Application.Current.Resources["SegmentedPillActiveButtonStyle"];
        var normalStyle = (Style)Application.Current.Resources["SegmentedPillButtonStyle"];

        FilterBtnAll.Style = _currentCategory == "ALL" ? activeStyle : normalStyle;
        FilterBtnLatency.Style = _currentCategory == "LATENCY" ? activeStyle : normalStyle;
        FilterBtnGaming.Style = _currentCategory == "GAMING" ? activeStyle : normalStyle;
        FilterBtnDisplay.Style = _currentCategory == "DISPLAY" ? activeStyle : normalStyle;
        FilterBtnPrivacy.Style = _currentCategory == "PRIVACY" ? activeStyle : normalStyle;
        FilterBtnSystem.Style = _currentCategory == "SYSTEM" ? activeStyle : normalStyle;
        FilterBtnNetwork.Style = _currentCategory == "NETWORK" ? activeStyle : normalStyle;

        ApplyFilter();
    }

    private void BtnPreviewTweak_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var res = TweakCatalog.ExecuteDryRun(id);
        if (res.Changes is { Count: > 0 })
        {
            string summary = string.Join("  |  ", res.Changes.Select(c => $"{c.Target}: {c.Before} -> {c.After}"));
            ShowAlert($"[DRY-RUN] {id}: {summary}", "\uE890");
        }
        else
        {
            ShowAlert($"[DRY-RUN] {id}: {res.Message}", "\uE783");
        }
    }

    private async void TweakToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvents) return;
        if (sender is not ToggleSwitch toggle || toggle.Tag is not string id || !toggle.IsLoaded) return;

        var item = _allTweaks.FirstOrDefault(t => t.Id == id);
        if (item == null || item.IsEnabled == toggle.IsOn) return;

        bool desired = toggle.IsOn;
        if (desired && ChkDryRun.IsChecked == true)
        {
            var preview = TweakCatalog.ExecuteDryRun(id);
            string diff = preview.Changes != null
                ? string.Join("\n", preview.Changes.Select(c => $"• {c.Target}: {c.Before} → {c.After}"))
                : preview.Message;
            ShowAlert($"[DRY-RUN ONLY] {item.Title}: {diff}", "\uE890");
            _suppressToggleEvents = true;
            toggle.IsOn = false;
            _suppressToggleEvents = false;
            return;
        }

        TweakExecutionResult result = await Task.Run(() =>
            desired ? TweakCatalog.ApplyTweak(id) : TweakCatalog.RevertTweak(id));

        await RefreshLiveStatesAsync();

        if (result.Status is TweakStatus.Success or TweakStatus.AlreadyApplied or TweakStatus.NeedsReboot)
        {
            ShowAlert(result.Message, result.Status == TweakStatus.NeedsReboot ? "\uE777" : "\uE73E");
            App.ViewModel?.AddLog("TWEAK", $"{item.Title}: {result.Message}");
        }
        else
        {
            ShowAlert(result.Message, "\uE783");
            App.ViewModel?.AddLog("TWEAK", $"{item.Title}: {result.Message}", isError: true);
        }
    }

    private async void BtnRefreshState_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLiveStatesAsync();
        ShowAlert("Live OS state re-scanned across all catalog tweaks.", "\uE72C");
    }

    private async void BtnCreateRestorePoint_Click(object sender, RoutedEventArgs e)
    {
        BtnCreateRestorePoint.IsEnabled = false;
        ShowAlert("Creating Windows System Restore Point...", "\uE777");
        try
        {
            string result = await OptimizationEngine.CreateRestorePointAsync(CancellationToken.None);
            ShowAlert(result, "\uE73E");
            App.ViewModel?.AddLog("SECURITY", result);
        }
        finally
        {
            BtnCreateRestorePoint.IsEnabled = true;
        }
    }

    private async void BtnRevertAll_Click(object sender, RoutedEventArgs e)
    {
        var (ok, fail, errors) = await Task.Run(() => TweakBackupService.RevertAllSnapshots());
        await RefreshLiveStatesAsync();
        string msg = fail == 0
            ? $"Reverted {ok} snapshot(s) to original OS values."
            : $"Reverted {ok}, failed {fail}: {string.Join("; ", errors)}";
        ShowAlert(msg, fail == 0 ? "\uE73E" : "\uE783");
        App.ViewModel?.AddLog("REVERT", msg, fail > 0);
    }

    private Task ApplyPresetAsync(string presetName, string[] ids)
    {
        return RunPresetCoreAsync(presetName, ids);
    }

    private async Task RunPresetCoreAsync(string presetName, string[] ids)
    {
        if (ChkDryRun.IsChecked == true)
        {
            var previewLines = ids.Select(id =>
            {
                var r = TweakCatalog.ExecuteDryRun(id);
                return r.Changes != null ? $"{id} ({r.Changes.Count} change(s))" : $"{id} ({r.Status})";
            });
            ShowAlert($"[DRY-RUN '{presetName}'] Would apply: {string.Join(", ", previewLines)}", "\uE890");
            return;
        }

        int applied = 0, skipped = 0, failed = 0;
        await Task.Run(() =>
        {
            foreach (string id in ids)
            {
                var res = TweakCatalog.ApplyTweak(id);
                if (res.Status is TweakStatus.Success or TweakStatus.NeedsReboot) applied++;
                else if (res.Status is TweakStatus.AlreadyApplied or TweakStatus.NotApplicable) skipped++;
                else failed++;
            }
        });

        await RefreshLiveStatesAsync();
        string summary = $"Preset '{presetName}': {applied} applied, {skipped} already/N/A, {failed} failed/need admin.";
        ShowAlert(summary, failed == 0 ? "\uE73E" : "\uE783");
        App.ViewModel?.AddLog("PRESET", summary, failed > 0);
    }

    private async void PresetEsports_Click(object sender, RoutedEventArgs e) =>
        await ApplyPresetAsync("Gaming / Esports", new[] { "power", "core_parking", "system_responsiveness", "network_throttling", "mouse_fix", "keyboard_fix", "game_mode", "game_dvr", "gpu_pipeline", "windowed_game_opt", "net_tcp_low_latency" });

    private async void PresetPrivacy_Click(object sender, RoutedEventArgs e) =>
        await ApplyPresetAsync("Privacy & AI Shield", new[] { "telemetry", "copilot_disable", "recall_disable", "office_telemetry", "cortana", "taskbar_clutter", "privacy_advertising", "privacy_clipboard" });

    private async void PresetBalanced_Click(object sender, RoutedEventArgs e) =>
        await ApplyPresetAsync("Balanced / Work", new[] { "visuals", "office_telemetry", "privacy_advertising", "taskbar_clutter" });

    private async void PresetLaptop_Click(object sender, RoutedEventArgs e) =>
        await ApplyPresetAsync("Laptop / Battery", new[] { "onedrive_disable", "telemetry", "taskbar_clutter", "visuals" });

    private void PresetStock_Click(object sender, RoutedEventArgs e) => BtnRevertAll_Click(sender, e);

    private void ShowAlert(string message, string iconGlyph)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AlertBanner.Visibility = Visibility.Visible;
            AlertText.Text = message;
            AlertIcon.Text = iconGlyph;
        });
    }

    private void AlertBannerClose_Click(object sender, RoutedEventArgs e) =>
        AlertBanner.Visibility = Visibility.Collapsed;
}
