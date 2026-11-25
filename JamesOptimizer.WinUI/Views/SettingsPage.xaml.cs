using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using JamesOptimizer.WinUI.Core;
using JamesOptimizer.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace JamesOptimizer.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    private bool _isInitializing = true;
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "JamesOptimizer";

    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        MotionHelper.AnimateStaggeredEntrance(new UIElement[]
        {
            CardService, CardAppearance, CardNetworkBench, CardAbout
        }, staggerMs: 45, durationMs: 260);

        _isInitializing = true;
        ComboTheme.SelectedIndex = 0;
        ToggleReduceAnimations.IsOn = MotionHelper.ReduceAnimations;
        ToggleMinimizeToTray.IsOn = TrayIconHelper.MinimizeToTray;
        await CheckServiceStatusAsync();
        await LoadSmartRamUiAsync();
        LoadStartupState();
        LoadAutoTimerState();
        LoadUpdatePrefs();
        _isInitializing = false;
    }

    private async Task LoadSmartRamUiAsync()
    {
        var mem = NativeSystemHelper.GetMemoryBreakdown();
        TxtMemoryBreakdown.Text = $"Live RAM: {mem.AvailablePhysicalMb:F0} MB Free · ~{mem.StandbyEstimateMb:F0} MB Standby · {mem.CommittedMb:F0} / {mem.CommitLimitMb:F0} MB Committed.";

        var cfg = await BackgroundServiceClient.GetSmartRamSettingsAsync();
        if (cfg != null)
        {
            ToggleSmartRam.IsOn = cfg.Enabled;
            ChkPauseFullscreen.IsChecked = cfg.PauseDuringFullscreen;
            foreach (var item in ComboSmartRamThreshold.Items)
            {
                if (item is ComboBoxItem cbi && int.TryParse(cbi.Tag?.ToString(), out int v) && v == cfg.FreeRamThresholdMb)
                {
                    ComboSmartRamThreshold.SelectedItem = cbi;
                    break;
                }
            }
        }
    }

    private void SmartRam_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _ = SaveSmartRamAsync();
    }

    private async void BtnSaveSmartRam_Click(object sender, RoutedEventArgs e) => await SaveSmartRamAsync();

    private async Task SaveSmartRamAsync()
    {
        int thresh = int.TryParse((ComboSmartRamThreshold.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int t) ? t : 2048;
        var cfg = new SmartRamSettings
        {
            Enabled = ToggleSmartRam.IsOn,
            FreeRamThresholdMb = thresh,
            MinStandbyMb = Math.Max(512, thresh / 2),
            PauseDuringFullscreen = ChkPauseFullscreen.IsChecked == true
        };
        bool ok = await BackgroundServiceClient.SetSmartRamSettingsAsync(cfg);
        MainWindow.Current?.ShowToast(ok ? "Smart RAM Saved" : "Service Offline",
            ok ? $"Purge threshold < {thresh} MB Free synced." : "Start JamesOptimizer.Service to enable Smart RAM daemon.",
            ok ? "State.Good" : "State.Warn");
    }

    private void ToggleReduceAnimations_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        MotionHelper.ReduceAnimations = ToggleReduceAnimations.IsOn;
        MainWindow.Current?.ShowToast("Motion Preference Updated", ToggleReduceAnimations.IsOn ? "Reduced animations enabled." : "Effects enabled (30fps cap).", "Accent.Purple");
    }

    private void ToggleMinimizeToTray_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        TrayIconHelper.MinimizeToTray = ToggleMinimizeToTray.IsOn;
        MainWindow.Current?.ShowToast("System Tray Behavior", ToggleMinimizeToTray.IsOn ? "Closing window minimizes to tray." : "Closing window exits app.", "Accent.Cyan");
    }

    private async void BtnTestPing_Click(object sender, RoutedEventArgs e)
    {
        BtnTestPing.IsEnabled = false;
        TxtPingResult.Text = "Pinging 1.1.1.1...";
        long ms = await OptimizationEngine.TestPingAsync("1.1.1.1");
        TxtPingResult.Text = ms >= 0 ? $"Latency: {ms} ms (1.1.1.1)" : "Ping timed out or unreachable.";
        BtnTestPing.IsEnabled = true;
    }

    private async Task CheckServiceStatusAsync()
    {
        TxtServiceStatus.Text = "Checking \\\\.\\pipe\\JamesOptimizerPipe...";
        var (ok, msg) = await BackgroundServiceClient.HelloHandshakeAsync();
        if (ok)
            TxtServiceStatus.Text = $"Active · {msg}";
        else
            TxtServiceStatus.Text = $"Service Idle / Admin Pipe Unavailable ({msg})";
    }

    private async void BtnPingService_Click(object sender, RoutedEventArgs e)
    {
        BtnPingService.IsEnabled = false;
        try { await CheckServiceStatusAsync(); }
        finally { BtnPingService.IsEnabled = true; }
    }

    private void LoadStartupState()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            ToggleStartup.IsOn = key?.GetValue(AppName) != null;
        }
        catch { ToggleStartup.IsOn = false; }
    }

    private void ToggleStartup_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return;
            if (ToggleStartup.IsOn && !string.IsNullOrEmpty(Environment.ProcessPath))
                key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(AppName, false);
        }
        catch { }
    }

    private void LoadAutoTimerState()
    {
        try
        {
            int timerMinutes = Convert.ToInt32(Registry.GetValue(CoreConstants.RegistryConfigRoot, "AutoTimer", 0) ?? 0);
            ComboAutoTimer.SelectedIndex = timerMinutes switch { 15 => 1, 30 => 2, 60 => 3, 120 => 4, _ => 0 };
        }
        catch { ComboAutoTimer.SelectedIndex = 0; }
    }

    private async void ComboAutoTimer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ComboAutoTimer.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int minutes))
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\JamesOptimizer");
                key?.SetValue("AutoTimer", minutes, RegistryValueKind.DWord);
            }
            catch { }
            await BackgroundServiceClient.SetAutoTimerAsync(minutes);
        }
    }

    private void LoadUpdatePrefs()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(CoreConstants.UserConfigSubKey, false);
            ToggleOptInUpdates.IsOn = (key?.GetValue("OptInUpdateCheck") as int? ?? 0) == 1;
            string ch = key?.GetValue("ReleaseChannel")?.ToString() ?? "Stable";
            ComboReleaseChannel.SelectedIndex = ch.Equals("Beta", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        catch { }
    }

    private void UpdatePrefs_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(CoreConstants.UserConfigSubKey);
            key?.SetValue("OptInUpdateCheck", ToggleOptInUpdates.IsOn ? 1 : 0, RegistryValueKind.DWord);
            string ch = (ComboReleaseChannel.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Stable";
            key?.SetValue("ReleaseChannel", ch, RegistryValueKind.String);
        }
        catch { }
    }

    private void ComboTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ComboTheme.SelectedItem is ComboBoxItem item && MainWindow.Current?.Content is FrameworkElement root)
        {
            root.RequestedTheme = (item.Tag?.ToString() ?? "Dark") switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }

    #region JSON Profile Export/Import & Panic Revert

    private async void BtnExportProfile_Click(object sender, RoutedEventArgs e)
    {
        string path = (TxtProfilePath.Text ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"JamesOptimizer_Profile_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            TxtProfilePath.Text = path;
        }

        var gameCfg = await BackgroundServiceClient.GetGameProfilesAsync();
        var ramCfg = await BackgroundServiceClient.GetSmartRamSettingsAsync();
        var profile = ProfileConfigService.ExportCurrentProfile(gameCfg, ramCfg);
        ProfileConfigService.SaveProfileToFile(path, profile);

        TxtProfileStatus.Text = $"Exported {profile.EnabledTweakIds.Count} active tweak(s) with SHA-256 digest to '{path}'.";
        MainWindow.Current?.ShowToast("Profile Exported", $"{profile.EnabledTweakIds.Count} tweaks saved.", "State.Good");
    }

    private void BtnPreviewImportProfile_Click(object sender, RoutedEventArgs e)
    {
        string path = (TxtProfilePath.Text ?? "").Trim().Trim('"');
        if (!File.Exists(path)) { TxtProfileStatus.Text = "Profile JSON file not found."; return; }

        var preview = ProfileConfigService.PreviewImport(File.ReadAllText(path));
        if (!preview.IsValid)
        {
            TxtProfileStatus.Text = $"Invalid profile: {preview.ErrorMessage}";
            return;
        }
        TxtProfileStatus.Text = $"[DRY-RUN] {preview.ToApply.Count} tweak(s) will be applied ({string.Join(", ", preview.ToApply)}), {preview.AlreadyApplied.Count} already active, {preview.SkippedUnknownOrIncompatible.Count} skipped.";
    }

    private async void BtnApplyImportProfile_Click(object sender, RoutedEventArgs e)
    {
        string path = (TxtProfilePath.Text ?? "").Trim().Trim('"');
        if (!File.Exists(path)) { TxtProfileStatus.Text = "Profile JSON file not found."; return; }

        var preview = ProfileConfigService.PreviewImport(File.ReadAllText(path));
        if (!preview.IsValid) { TxtProfileStatus.Text = preview.ErrorMessage; return; }

        var (applied, failed, _) = await Task.Run(() => ProfileConfigService.ApplyPreviewedProfile(preview));
        TxtProfileStatus.Text = $"Imported profile: {applied} tweak(s) applied, {failed} failed/need admin.";
        MainWindow.Current?.ShowToast("Profile Applied", $"{applied} tweak(s) applied cleanly.", failed == 0 ? "State.Good" : "State.Warn");
    }

    private async void BtnPanicRevert_Click(object sender, RoutedEventArgs e)
    {
        var (ok, fail, _) = await Task.Run(() => TweakBackupService.RevertAllSnapshots());
        TxtProfileStatus.Text = $"PANIC REVERT: {ok} snapshot(s) restored to prior state ({fail} failed).";
        MainWindow.Current?.ShowToast("Panic Revert Complete", $"Reverted {ok} snapshot(s).", fail == 0 ? "State.Good" : "State.Warn");
    }

    #endregion

    private void BtnOpenGithub_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/jamesgod1461/JamesOptimizer");
    private void BtnReportIssue_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/jamesgod1461/JamesOptimizer/issues");
    private void BtnOpenDiscussions_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/jamesgod1461/JamesOptimizer/discussions");

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
    }
}
