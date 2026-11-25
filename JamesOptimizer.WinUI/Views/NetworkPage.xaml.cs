using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using JamesOptimizer.WinUI.Core;
using JamesOptimizer.WinUI.Theme;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JamesOptimizer.WinUI.Views;

public sealed partial class NetworkPage : Page
{
    private DispatcherTimer? _netTimer;
    private long _prevRx, _prevTx;
    private readonly List<float> _rxHistory = new();
    private List<NetworkInterface> _adapters = new();

    private static readonly CanvasStrokeStyle _lineStroke = new()
    {
        StartCap = CanvasCapStyle.Round,
        EndCap = CanvasCapStyle.Round,
        LineJoin = CanvasLineJoin.Round
    };
    private static readonly CanvasTextFormat _axisFormat = new()
    {
        FontFamily = "Cascadia Mono, Consolas",
        FontSize = 9.5f
    };

    public ObservableCollection<DnsBenchmarkResult> DnsBenchResults { get; } = new();
    public ObservableCollection<HostsEntry> HostsEntries { get; } = new();
    public ObservableCollection<SocketProcessEntry> SocketEntries { get; } = new();

    public NetworkPage()
    {
        InitializeComponent();
        for (int i = 0; i < 24; i++) _rxHistory.Add(0f);
        DnsBenchList.ItemsSource = DnsBenchResults;
        HostsList.ItemsSource = HostsEntries;
        SocketsList.ItemsSource = SocketEntries;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        MotionHelper.AnimateStaggeredEntrance(new UIElement[] { this }, staggerMs: 0, durationMs: 160);
        RefreshActiveAdapters();
        SampleThroughput(initial: true);

        _netTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _netTimer.Tick += (_, _) => SampleThroughput(initial: false);
        _netTimer.Start();

        ThemeEffects.WindowVisibilityChanged -= OnWindowVisibilityChanged;
        ThemeEffects.WindowVisibilityChanged += OnWindowVisibilityChanged;
    }

    private void OnWindowVisibilityChanged(object? sender, bool visibleAndActive)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (visibleAndActive) _netTimer?.Start();
            else _netTimer?.Stop();
        });
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // BUG FIX: Release Win2D DXGI device reference to prevent graphics resource leak on navigation.
        NetSparkCanvas?.RemoveFromVisualTree();

        ThemeEffects.WindowVisibilityChanged -= OnWindowVisibilityChanged;
        _netTimer?.Stop();
        _netTimer = null;
    }

    private void NetTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        var active = (Style)Application.Current.Resources["SegmentedPillActiveButtonStyle"];
        var normal = (Style)Application.Current.Resources["SegmentedPillButtonStyle"];
        TabDns.Style = tag == "DNS" ? active : normal;
        TabHosts.Style = tag == "HOSTS" ? active : normal;
        TabSockets.Style = tag == "SOCKETS" ? active : normal;

        PanelDns.Visibility = tag == "DNS" ? Visibility.Visible : Visibility.Collapsed;
        PanelHosts.Visibility = tag == "HOSTS" ? Visibility.Visible : Visibility.Collapsed;
        PanelSockets.Visibility = tag == "SOCKETS" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "HOSTS" && HostsEntries.Count == 0) ReloadHosts();
        if (tag == "SOCKETS" && SocketEntries.Count == 0) RefreshSockets();
    }

    private void RefreshActiveAdapters()
    {
        _adapters = NetworkDiagnosticsService.GetActivePhysicalAdapters();
        ComboAdapter.Items.Clear();
        foreach (var nic in _adapters)
            ComboAdapter.Items.Add(new ComboBoxItem { Content = nic.Name, Tag = nic.Name });

        if (ComboAdapter.Items.Count > 0)
            ComboAdapter.SelectedIndex = 0;
        UpdateSelectedAdapterDnsLabel();
    }

    private void ComboAdapter_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectedAdapterDnsLabel();

    private void UpdateSelectedAdapterDnsLabel()
    {
        string? name = (ComboAdapter.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var nic = _adapters.FirstOrDefault(a => a.Name == name) ?? _adapters.FirstOrDefault();
        if (nic == null)
        {
            TxtActiveDns.Text = "No active physical adapter";
            return;
        }
        var dns = nic.GetIPProperties().DnsAddresses
            .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(ip => ip.ToString())
            .Take(2);
        string dnsStr = string.Join(", ", dns);
        TxtActiveDns.Text = string.IsNullOrEmpty(dnsStr) ? "DNS: Automatic" : $"DNS: {dnsStr}";
    }

    private void SampleThroughput(bool initial)
    {
        try
        {
            long rx = 0, tx = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var stats = nic.GetIPStatistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }

            if (!initial && rx >= _prevRx && tx >= _prevTx)
            {
                double dlKb = (rx - _prevRx) / 1024.0;
                double ulKb = (tx - _prevTx) / 1024.0;
                TxtDownloadRate.Text = dlKb >= 1024 ? $"{dlKb / 1024.0:F1} MB/s" : $"{dlKb:0} KB/s";
                TxtUploadRate.Text = ulKb >= 1024 ? $"{ulKb / 1024.0:F1} MB/s" : $"{ulKb:0} KB/s";

                while (_rxHistory.Count >= 24) _rxHistory.RemoveAt(0);
                _rxHistory.Add((float)Math.Clamp(dlKb, 0, 10240));
                NetSparkCanvas?.Invalidate();
            }

            _prevRx = rx;
            _prevTx = tx;
        }
        catch { }
    }

    private void NetSparkCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Antialiasing = CanvasAntialiasing.Antialiased;
        float w = (float)sender.ActualWidth;
        float h = (float)sender.ActualHeight;
        if (w <= 0 || h <= 0 || _rxHistory.Count < 2) return;

        var gridColor = ThemeEffects.GetColor("Border.Subtle.Color", ColorHelper.FromArgb(20, 255, 255, 255));
        var labelColor = ThemeEffects.GetColor("Text.Tertiary.Color", ColorHelper.FromArgb(255, 107, 107, 107));
        var cyan = ThemeEffects.GetColor("Accent.Cyan.Color", ColorHelper.FromArgb(255, 34, 211, 238));
        var cyanGlow = ThemeEffects.GetColor("Accent.Cyan.GlowColor", ColorHelper.FromArgb(77, 34, 211, 238));

        // Neutral gridlines at Border.Subtle & mono axis label in Text.Tertiary
        ds.DrawLine(0f, h * 0.5f, w, h * 0.5f, gridColor, 1f);
        ds.DrawLine(0f, h - 1f, w, h - 1f, gridColor, 1f);

        float max = Math.Max(64f, _rxHistory.Max());
        string maxLabel = max >= 1024f ? $"{max / 1024f:F1} MB/s" : $"{max:0} KB/s";
        ds.DrawText(maxLabel, 2f, 0f, labelColor, _axisFormat);

        float stepX = w / (_rxHistory.Count - 1);
        using var pb = new CanvasPathBuilder(ds);
        for (int i = 0; i < _rxHistory.Count; i++)
        {
            float x = i * stepX;
            float y = h - (_rxHistory[i] / max * (h - 8f)) - 4f;
            if (i == 0) pb.BeginFigure(new Vector2(x, y));
            else pb.AddLine(new Vector2(x, y));
        }
        pb.EndFigure(CanvasFigureLoop.Open);
        using var geom = CanvasGeometry.CreatePath(pb);
        if (ThemeEffects.EffectsEnabled)
            ds.DrawGeometry(geom, cyanGlow, 5f, _lineStroke);
        ds.DrawGeometry(geom, cyan, 1.8f, _lineStroke);
    }

    private async void BtnRunDnsBench_Click(object sender, RoutedEventArgs e)
    {
        BtnRunDnsBench.IsEnabled = false;
        DnsBenchResults.Clear();

        var targets = NetworkDiagnosticsService.Presets.Select(p => (p.Name, p.PrimaryV4)).ToList();
        var results = await NetworkDiagnosticsService.BenchmarkDnsServersAsync(targets, repetitions: 5);
        foreach (var r in results) DnsBenchResults.Add(r);

        BtnRunDnsBench.IsEnabled = true;
        App.ViewModel?.AddLog("NETWORK", $"DNS benchmark finished. Fastest: {results.FirstOrDefault()?.ProviderName ?? "N/A"}.");
    }

    private async void BtnFlushDns_Click(object sender, RoutedEventArgs e)
    {
        await OptimizationEngine.FlushDnsAsync();
        App.ViewModel?.AddLog("NETWORK", "DNS resolver cache flushed natively.");
        MainWindow.Current?.ShowToast("DNS Cache Flushed", "Windows DNS resolver cleared.", "State.Good");
    }

    private async void BtnResetNetwork_Click(object sender, RoutedEventArgs e)
    {
        await OptimizationEngine.ResetTcpIpStackAsync();
        App.ViewModel?.AddLog("NETWORK", "TCP/IP stack and Winsock catalog reset.");
        MainWindow.Current?.ShowToast("Network Stack Reset", "TCP/IP & Winsock reset cleanly.", "Accent.Cyan");
    }

    private async void BtnSetDns_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string provider) return;
        btn.IsEnabled = false;

        string? adapter = (ComboAdapter.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var preset = NetworkDiagnosticsService.Presets.FirstOrDefault(p => p.Name.Equals(provider, StringComparison.OrdinalIgnoreCase));

        await OptimizationEngine.ApplyDnsAsync(preset?.PrimaryV4 ?? "", preset?.SecondaryV4 ?? "", adapter);
        if (ChkEnableDoh.IsChecked == true && preset != null)
            await NetworkDiagnosticsService.EnableWindows11DohAsync(preset.PrimaryV4, preset.DohTemplate);

        btn.IsEnabled = true;
        RefreshActiveAdapters();
        MainWindow.Current?.ShowToast("DNS Updated", $"Applied {provider} on {adapter ?? "active adapter"}.", "State.Good");
    }

    #region HOSTS Editor

    private void ReloadHosts()
    {
        HostsEntries.Clear();
        foreach (var entry in HostsManagerService.ReadEntries())
            HostsEntries.Add(entry);
        TxtHostsStatus.Text = $"Loaded {HostsEntries.Count} entry/entries from {HostsManagerService.HostsFilePath}.";
    }

    private void BtnReloadHosts_Click(object sender, RoutedEventArgs e) => ReloadHosts();

    private void BtnAddHosts_Click(object sender, RoutedEventArgs e)
    {
        string domain = TxtHostsDomain.Text?.Trim() ?? "";
        if (HostsManagerService.IsSensitiveDomain(domain))
        {
            MainWindow.Current?.ShowToast("Caution: Core Windows Domain", $"'{domain}' is required for Windows Update/Store/Auth.", "State.Warn");
        }

        var (ok, msg) = HostsManagerService.AddOrUpdateEntry(TxtHostsIp.Text, domain, TxtHostsComment.Text, TxtHostsTag.Text);
        TxtHostsStatus.Text = msg;
        if (ok)
        {
            TxtHostsDomain.Text = "";
            TxtHostsComment.Text = "";
            ReloadHosts();
        }
        MainWindow.Current?.ShowToast(ok ? "HOSTS Updated" : "HOSTS Error", msg, ok ? "State.Good" : "State.Bad");
    }

    private void HostsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch ts || ts.Tag is not int lineIdx || !ts.IsLoaded) return;
        var entry = HostsEntries.FirstOrDefault(x => x.LineIndex == lineIdx);
        if (entry == null || entry.Enabled == ts.IsOn) return;

        var (ok, msg) = HostsManagerService.SetEntryEnabled(lineIdx, ts.IsOn);
        TxtHostsStatus.Text = msg;
        if (ok)
            ReloadHosts();
        else
        {
            ts.IsOn = entry.Enabled;
            MainWindow.Current?.ShowToast("HOSTS Update Failed", msg, "State.Bad");
        }
    }

    private void BtnDeleteHosts_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int lineIdx) return;
        var (ok, msg) = HostsManagerService.RemoveEntry(lineIdx);
        TxtHostsStatus.Text = msg;
        if (ok) ReloadHosts();
    }

    private void BtnRestoreHosts_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = HostsManagerService.RestoreLatestBackup();
        TxtHostsStatus.Text = msg;
        if (ok) ReloadHosts();
        MainWindow.Current?.ShowToast(ok ? "HOSTS Restored" : "Restore Failed", msg, ok ? "State.Good" : "State.Bad");
    }

    #endregion

    #region Active Connections

    private void RefreshSockets()
    {
        SocketEntries.Clear();
        var rows = NetworkDiagnosticsService.GetActiveTcpConnectionsWithPid();
        foreach (var r in rows.Take(120))
            SocketEntries.Add(r);
        TxtSocketsStatus.Text = $"Showing {SocketEntries.Count} of {rows.Count} active TCP sockets.";
    }

    private void BtnRefreshSockets_Click(object sender, RoutedEventArgs e) => RefreshSockets();

    private void BtnOpenSocketLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    #endregion
}
