using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using JamesOptimizer.WinUI.Models;
using JamesOptimizer.WinUI.Services;

namespace JamesOptimizer.WinUI.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class DashboardViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISystemMetricsService _metricsService;
    private readonly DispatcherQueue _dispatcherQueue;

    private int _overallScore = 96;
    private int _healthScore = 98;
    private int _perfScore = 94;
    private int _secScore = 99;
    private int _stabScore = 95;

    private string _currentConsoleMarquee = "[SYSTEM] Ready. All kernel drivers and memory monitors active.";
    private string _activeConsoleFilter = "ALL";
    private bool _autoRunEnabled = false;
    private bool _isSidebarExpanded = true;
    private bool _isOptimizing = false;
    private string _lastOptimizationStatus = "PRO SUITE · 100% HEALTHY";

    private readonly SolidColorBrush _cyanBrush = new(ColorHelper.FromArgb(255, 0, 245, 255));
    private readonly SolidColorBrush _violetBrush = new(ColorHelper.FromArgb(255, 123, 92, 240));
    private readonly SolidColorBrush _greenBrush = new(ColorHelper.FromArgb(255, 0, 230, 118));
    private readonly SolidColorBrush _amberBrush = new(ColorHelper.FromArgb(255, 255, 179, 0));
    private readonly SolidColorBrush _redBrush = new(ColorHelper.FromArgb(255, 255, 61, 87));

    public MetricItem CpuMetric { get; }
    public MetricItem GpuMetric { get; }
    public MetricItem RamMetric { get; }
    public MetricItem DiskMetric { get; }

    public ObservableCollection<SubMetricBadge> SubMetrics { get; } = new();
    public ObservableCollection<DriveMetric> Drives { get; } = new();
    public ObservableCollection<QuickActionItem> QuickActions { get; } = new();
    public ObservableCollection<LogEntry> AllLogs { get; } = new();
    public ObservableCollection<LogEntry> DisplayedLogs { get; } = new();

    public int OverallScore
    {
        get => _overallScore;
        set { _overallScore = value; OnPropertyChanged(); }
    }

    public string CurrentConsoleMarquee
    {
        get => _currentConsoleMarquee;
        set { _currentConsoleMarquee = value; OnPropertyChanged(); }
    }

    public string ActiveConsoleFilter
    {
        get => _activeConsoleFilter;
        set
        {
            if (_activeConsoleFilter != value)
            {
                _activeConsoleFilter = value;
                OnPropertyChanged();
                ApplyConsoleFilter();
            }
        }
    }

    private readonly BackgroundServiceClient _serviceClient = new();

    public bool AutoRunEnabled
    {
        get => _autoRunEnabled;
        set
        {
            if (_autoRunEnabled != value)
            {
                _autoRunEnabled = value;
                OnPropertyChanged();
                int timerMinutes = value ? 30 : 0;
                AddLog("OPTIMIZER", $"Auto-Run background interval set to: {(value ? "Active (Every 30m)" : "Off")}");
                _ = Task.Run(async () =>
                {
                    bool ok = await BackgroundServiceClient.SetAutoTimerAsync(timerMinutes);
                    if (ok)
                    {
                        AddLog("SERVICE", $"Background Service AutoTimer synchronized to {timerMinutes}m.");
                    }
                });
            }
        }
    }

    public bool IsSidebarExpanded
    {
        get => _isSidebarExpanded;
        set { _isSidebarExpanded = value; OnPropertyChanged(); }
    }

    public bool IsOptimizing
    {
        get => _isOptimizing;
        set { _isOptimizing = value; OnPropertyChanged(); }
    }

    public string LastOptimizationStatus
    {
        get => _lastOptimizationStatus;
        set { _lastOptimizationStatus = value; OnPropertyChanged(); }
    }

    public ICommand RunFullOptimizationCommand { get; }
    public ICommand RunQuickActionCommand { get; }
    public ICommand FilterConsoleCommand { get; }
    public ICommand CopyConsoleLogsCommand { get; }
    public ICommand ClearConsoleLogsCommand { get; }
    public ICommand ToggleSidebarCommand { get; }

    public DashboardViewModel() : this(new SystemMetricsService(), DispatcherQueue.GetForCurrentThread() ?? DispatcherQueue.GetForCurrentThread())
    {
    }

    public DashboardViewModel(ISystemMetricsService metricsService, DispatcherQueue? dispatcherQueue = null)
    {
        _metricsService = metricsService;
        _dispatcherQueue = dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();

        // 1. Initialize Metric Items with distinct colors and emojis
        CpuMetric = new MetricItem { Name = "CPU", Icon = "💻", AccentBrush = _cyanBrush, Value = 14f };
        GpuMetric = new MetricItem { Name = "GPU", Icon = "🎮", AccentBrush = _violetBrush, Value = 18f };
        RamMetric = new MetricItem { Name = "RAM", Icon = "🧠", AccentBrush = _greenBrush, Value = 57f, SubLabel = "18.3 / 31.8 GB" };
        DiskMetric = new MetricItem { Name = "DISK", Icon = "💾", AccentBrush = _amberBrush, Value = 4f };

        // Prepopulate sparkline history with 30 initial points
        for (int i = 0; i < 30; i++)
        {
            CpuMetric.History.Add(12f + (i % 6));
            GpuMetric.History.Add(15f + (i % 4));
            RamMetric.History.Add(56f + (i % 3));
            DiskMetric.History.Add(3f + (i % 5));
        }

        UpdateMetricValueBrushes();

        // 2. Initialize Sub-metrics pills (Health, Performance, Security, Stability)
        InitSubMetrics();

        // 3. Initialize Quick Actions (3x2 Grid)
        InitQuickActions();

        // 4. Initialize Drives
        InitDrives();

        // 5. Initialize Commands
        RunFullOptimizationCommand = new RelayCommand(async _ => await RunFullOptimizationAsync());
        RunQuickActionCommand = new RelayCommand(async param => await ExecuteQuickActionAsync(param as string ?? "ram_purge"));
        FilterConsoleCommand = new RelayCommand(param => ActiveConsoleFilter = param as string ?? "ALL");
        CopyConsoleLogsCommand = new RelayCommand(_ => CopyLogsToClipboard());
        ClearConsoleLogsCommand = new RelayCommand(_ => ClearLogs());
        ToggleSidebarCommand = new RelayCommand(_ => IsSidebarExpanded = !IsSidebarExpanded);

        // 6. Hook Service Events
        _metricsService.SnapshotUpdated += OnSnapshotUpdated;
        _metricsService.Start();

        // Initial Logs
        AddLog("SYSTEM", "JamesOptimizer PRO Kernel Initialized. Ready for power execution.");
        AddLog("KERNEL", "MicaAltController backdrop engaged. Windows 11 64-bit platform detected.");
        AddLog("OPTIMIZER", "Standby list monitoring started. Hardware polling interval 1000ms.");

        _ = Task.Run(async () =>
        {
            var status = await BackgroundServiceClient.GetStatusAsync();
            if (status.StartsWith("STATUS:", StringComparison.OrdinalIgnoreCase))
            {
                AddLog("SERVICE", $"Connected to Background Service ({status[7..]})");
            }
            else
            {
                AddLog("SERVICE", "Background Service: Standby / Independent execution mode.");
            }
        });
    }

    private void InitSubMetrics()
    {
        SubMetrics.Clear();
        SubMetrics.Add(new SubMetricBadge { Name = "Health", Percentage = _healthScore, AccentBrush = _greenBrush });
        SubMetrics.Add(new SubMetricBadge { Name = "Performance", Percentage = _perfScore, AccentBrush = _cyanBrush });
        SubMetrics.Add(new SubMetricBadge { Name = "Security", Percentage = _secScore, AccentBrush = _violetBrush });
        SubMetrics.Add(new SubMetricBadge { Name = "Stability", Percentage = _stabScore, AccentBrush = _amberBrush });
    }

    private void InitQuickActions()
    {
        QuickActions.Clear();
        QuickActions.Add(new QuickActionItem
        {
            Id = "ram_purge",
            Glyph = "\uE74C", // Cleanup
            Title = "Flush Working Sets",
            Subtitle = "Purge standby list & cache heaps",
            AccentBrush = _cyanBrush,
            TintBrush = new SolidColorBrush(ColorHelper.FromArgb(26, 0, 245, 255))
        });
        QuickActions.Add(new QuickActionItem
        {
            Id = "game_mode",
            Glyph = "\uE950", // Speed
            Title = "Ultra Game Priority",
            Subtitle = "Lock core timer resolution to 0.5ms",
            AccentBrush = _violetBrush,
            TintBrush = new SolidColorBrush(ColorHelper.FromArgb(26, 123, 92, 240))
        });
        QuickActions.Add(new QuickActionItem
        {
            Id = "temp_clean",
            Glyph = "\uE74D", // Delete/Clean
            Title = "Zero-Waste Storage",
            Subtitle = "Scrub %TEMP% & shader buffers",
            AccentBrush = _amberBrush,
            TintBrush = new SolidColorBrush(ColorHelper.FromArgb(26, 255, 179, 0))
        });
        QuickActions.Add(new QuickActionItem
        {
            Id = "dns_boost",
            Glyph = "\uE770", // Network
            Title = "Low-Jitter DNS",
            Subtitle = "Route gaming sockets & flush DNS",
            AccentBrush = _greenBrush,
            TintBrush = new SolidColorBrush(ColorHelper.FromArgb(26, 0, 230, 118))
        });
        QuickActions.Add(new QuickActionItem
        {
            Id = "startup_shield",
            Glyph = "\uE902", // Defender/Shield
            Title = "Startup Shield",
            Subtitle = "Suppress rogue background loaders",
            AccentBrush = _cyanBrush,
            TintBrush = new SolidColorBrush(ColorHelper.FromArgb(26, 0, 245, 255))
        });
        QuickActions.Add(new QuickActionItem
        {
            Id = "telemetry_kill",
            Glyph = "\uE72E", // Privacy/Lock
            Title = "Telemetry Kill",
            Subtitle = "Terminate silent diagnostic spikes",
            AccentBrush = _redBrush,
            TintBrush = new SolidColorBrush(ColorHelper.FromArgb(26, 255, 61, 87))
        });
    }

    private void InitDrives()
    {
        Drives.Clear();
        var snapshots = _metricsService.GetDriveSnapshots();
        var colors = new[] { _cyanBrush, _violetBrush, _greenBrush };
        int colorIdx = 0;

        foreach (var s in snapshots)
        {
            double ratio = s.TotalGb > 0 ? (s.UsedGb / s.TotalGb) : 0;
            Drives.Add(new DriveMetric
            {
                DriveLetter = s.Name,
                VolumeLabel = s.VolumeLabel,
                TotalGb = s.TotalGb,
                UsedGb = s.UsedGb,
                FreeGb = s.FreeGb,
                UsageRatio = ratio,
                UsageSummary = $"{s.UsedGb:0} GB / {s.TotalGb:0} GB · {s.FreeGb:0} GB Free",
                FillBrush = colors[colorIdx % colors.Length]
            });
            colorIdx++;
        }
    }

    private void OnSnapshotUpdated(object? sender, SystemSnapshotEventArgs e)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            OverallScore = e.OverallScore;

            // Submetrics
            _healthScore = e.CalculatedHealthScore;
            _perfScore = e.CalculatedPerfScore;
            _secScore = e.CalculatedSecScore;
            _stabScore = e.CalculatedStabScore;

            if (SubMetrics.Count == 4)
            {
                SubMetrics[0].Percentage = _healthScore;
                SubMetrics[1].Percentage = _perfScore;
                SubMetrics[2].Percentage = _secScore;
                SubMetrics[3].Percentage = _stabScore;
            }

            // Update Metrics
            CpuMetric.Value = e.CpuPercent;
            GpuMetric.Value = e.GpuPercent;
            RamMetric.Value = e.RamPercent;
            RamMetric.SubLabel = $"{e.RamUsedGb:F1} / {e.RamTotalGb:F1} GB";
            DiskMetric.Value = e.DiskPercent;

            AppendSparklinePoint(CpuMetric.History, e.CpuPercent);
            AppendSparklinePoint(GpuMetric.History, e.GpuPercent);
            AppendSparklinePoint(RamMetric.History, e.RamPercent);
            AppendSparklinePoint(DiskMetric.History, e.DiskPercent);

            UpdateMetricValueBrushes();
        });
    }

    private static void AppendSparklinePoint(ObservableCollection<float> history, float point)
    {
        while (history.Count >= 30)
        {
            history.RemoveAt(0);
        }
        history.Add(point);
    }

    private void UpdateMetricValueBrushes()
    {
        CpuMetric.ValueBrush = GetThresholdBrush(CpuMetric.Value);
        GpuMetric.ValueBrush = GetThresholdBrush(GpuMetric.Value);
        RamMetric.ValueBrush = GetThresholdBrush(RamMetric.Value);
        DiskMetric.ValueBrush = GetThresholdBrush(DiskMetric.Value);
    }

    private SolidColorBrush GetThresholdBrush(float value)
    {
        // green <50%, amber 50–80%, red >80%
        if (value < 50f) return _greenBrush;
        if (value <= 80f) return _amberBrush;
        return _redBrush;
    }

    public async Task RunFullOptimizationAsync()
    {
        if (IsOptimizing) return;
        IsOptimizing = true;
        AddLog("OPTIMIZER", "Executing Full PRO System Optimization routine...");

        // Trigger background service optimization in parallel
        _ = Task.Run(async () =>
        {
            var svcRes = await BackgroundServiceClient.RequestOptimizationAsync();
            if (svcRes.StartsWith("SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                AddLog("SERVICE", svcRes);
            }
        });

        var result = await _metricsService.RunFullOptimizationAsync();
        IsOptimizing = false;

        LastOptimizationStatus = $"{result.Title} · {result.Message}";
        AddLog("OPTIMIZER", result.Message);
        CurrentConsoleMarquee = $"[SYSTEM] {result.Message}";
    }

    public async Task ExecuteQuickActionAsync(string actionId)
    {
        AddLog("OPTIMIZER", $"Triggered action '{actionId}'...");
        var result = await _metricsService.ExecuteQuickActionAsync(actionId);
        AddLog("OPTIMIZER", result.Message);
        CurrentConsoleMarquee = $"[ACTION] {result.Message}";
    }

    public void AddLog(string category, string message, bool isError = false)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            var entry = new LogEntry
            {
                Category = category,
                Message = message,
                IsError = isError
            };
            AllLogs.Insert(0, entry);
            if (AllLogs.Count > 100) AllLogs.RemoveAt(AllLogs.Count - 1);

            CurrentConsoleMarquee = $"[{category}] {message}";
            ApplyConsoleFilter();
        });
    }

    private void ApplyConsoleFilter()
    {
        DisplayedLogs.Clear();
        var filtered = ActiveConsoleFilter == "ALL"
            ? AllLogs
            : AllLogs.Where(l => l.Category.Equals(ActiveConsoleFilter, StringComparison.OrdinalIgnoreCase) ||
                                (ActiveConsoleFilter == "ERRORS" && l.IsError));

        foreach (var l in filtered.Take(40))
        {
            DisplayedLogs.Add(l);
        }
    }

    private void CopyLogsToClipboard()
    {
        try
        {
            var text = string.Join(Environment.NewLine, AllLogs.Select(l => l.FormattedText));
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            AddLog("SYSTEM", "Console log buffer copied to clipboard.");
        }
        catch { }
    }

    public void ClearLogs()
    {
        AllLogs.Clear();
        DisplayedLogs.Clear();
        AddLog("SYSTEM", "Console log buffer cleared.");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _metricsService.SnapshotUpdated -= OnSnapshotUpdated;
        _metricsService.Dispose();
    }
}
