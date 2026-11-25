using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using JamesOptimizer.WinUI.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace JamesOptimizer.WinUI.Views;

public sealed partial class GamingModePage : Page
{
    private bool _isBoosting = false;
    private bool _isInitializing = true;
    private GameAutomationConfig _gameConfig = new();
    public ObservableCollection<GameAutomationRule> GameRules { get; } = new();

    public GamingModePage()
    {
        InitializeComponent();
        GameRulesList.ItemsSource = GameRules;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        var remote = await BackgroundServiceClient.GetGameProfilesAsync();
        _gameConfig = remote ?? new GameAutomationConfig();
        ToggleAutoGameWatch.IsOn = _gameConfig.Enabled;
        GameRules.Clear();
        foreach (var r in _gameConfig.Rules) GameRules.Add(r);
        _isInitializing = false;
    }

    private async void ToggleAutoGameWatch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _gameConfig.Enabled = ToggleAutoGameWatch.IsOn;
        await SyncGameProfilesAsync();
    }

    private async void BtnAddGameRule_Click(object sender, RoutedEventArgs e)
    {
        string proc = (TxtNewGameProc.Text ?? "").Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(proc)) return;

        string disp = string.IsNullOrWhiteSpace(TxtNewGameDisplay.Text) ? proc : TxtNewGameDisplay.Text.Trim();
        string prio = (ComboNewGamePriority.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "High";

        var rule = new GameAutomationRule
        {
            ProcessName = proc,
            DisplayName = disp,
            ProcessPriority = prio,
            PurgeStandbyOnLaunch = ChkNewGamePurge.IsChecked == true
        };
        _gameConfig.Rules.RemoveAll(r => r.ProcessName.Equals(proc, StringComparison.OrdinalIgnoreCase));
        _gameConfig.Rules.Add(rule);
        GameRules.Clear();
        foreach (var r in _gameConfig.Rules) GameRules.Add(r);

        TxtNewGameProc.Text = "";
        TxtNewGameDisplay.Text = "";
        await SyncGameProfilesAsync();
    }

    private async void BtnRemoveGameRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string proc) return;
        _gameConfig.Rules.RemoveAll(r => r.ProcessName.Equals(proc, StringComparison.OrdinalIgnoreCase));
        var item = GameRules.FirstOrDefault(r => r.ProcessName.Equals(proc, StringComparison.OrdinalIgnoreCase));
        if (item != null) GameRules.Remove(item);
        await SyncGameProfilesAsync();
    }

    private async Task SyncGameProfilesAsync()
    {
        var res = await BackgroundServiceClient.SetGameProfilesAsync(_gameConfig);
        if (res.Success)
        {
            MainWindow.Current?.ShowToast("Per-Game Daemon Synced", $"{_gameConfig.Rules.Count} game rule(s) active on background service.", "Accent.Cyan");
        }
        else if (res.IsOffline)
        {
            string detail = string.IsNullOrWhiteSpace(res.Error) ? "Pipe unavailable." : res.Error;
            MainWindow.Current?.ShowToast("Service Offline", $"{detail} Start JamesOptimizer.Service to enable automatic game switching.", "State.Warn");
        }
        else
        {
            MainWindow.Current?.ShowToast("Service Rejected Update", res.Error ?? "Unknown error updating game profiles.", "State.Bad");
        }
    }

    private async void BtnActivateTurbo_Click(object sender, RoutedEventArgs e)
    {
        if (_isBoosting) return;
        _isBoosting = true;
        BtnActivateTurbo.IsEnabled = false;
        TxtTurboStatus.Text = "Engaging Game Turbo Engine...";

        try
        {
            await OptimizationEngine.RunGamingBoostAsync(CancellationToken.None);
            await OptimizationEngine.RunRamFlushAsync(CancellationToken.None);

            TxtStatus.Text = "TURBO ACTIVE ✓";
            var cyan = Theme.ThemeEffects.GetBrush("Accent.Cyan");
            StatusDot.Fill = cyan;
            TxtStatus.Foreground = cyan;
            TxtTurboBtn.Text = "GAME TURBO ENGAGED ✓";
            TxtTurboStatus.Text = "Low-latency gaming power & QoS engaged (revertable via Audit Drawer).";
            App.ViewModel?.AddLog("GAMING", "Game Turbo Engine engaged!");
        }
        catch (Exception ex)
        {
            BtnActivateTurbo.IsEnabled = true;
            TxtTurboBtn.Text = "ACTIVATE GAME TURBO ⚡";
            TxtTurboStatus.Text = $"Error: {ex.Message}";
            App.ViewModel?.AddLog("ERROR", $"Failed to engage Game Turbo: {ex.Message}", true);
        }
        finally
        {
            _isBoosting = false;
        }
    }

    private async void BtnGpuOptimize_Click(object sender, RoutedEventArgs e) => await RunCatalogActionAsync("gpu_pipeline", "HAGS GPU Scheduling");
    private async void BtnNetworkLatency_Click(object sender, RoutedEventArgs e) => await RunCatalogActionAsync("net_tcp_low_latency", "TCP Low Latency");
    private async void BtnDisableDvr_Click(object sender, RoutedEventArgs e) => await RunCatalogActionAsync("game_dvr", "Disable Game DVR");
    private async void BtnMousePrecision_Click(object sender, RoutedEventArgs e) => await RunCatalogActionAsync("mouse_fix", "1:1 Raw Mouse");
    private async void BtnKeyboardBuffer_Click(object sender, RoutedEventArgs e) => await RunCatalogActionAsync("keyboard_fix", "Keyboard Repeat");
    private async void BtnUltimatePower_Click(object sender, RoutedEventArgs e) => await RunCatalogActionAsync("power", "Ultimate Performance Power Plan");

    private static async Task RunCatalogActionAsync(string tweakId, string label)
    {
        var res = await Task.Run(() => TweakCatalog.ApplyTweak(tweakId));
        bool ok = res.Status is TweakStatus.Success or TweakStatus.AlreadyApplied or TweakStatus.NeedsReboot;
        App.ViewModel?.AddLog(ok ? "GAMING" : "ERROR", $"{label}: {res.Message}", !ok);
        MainWindow.Current?.ShowToast(label, res.Message, ok ? "State.Good" : "State.Bad");
    }
}
