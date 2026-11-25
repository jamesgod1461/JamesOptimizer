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
using JamesOptimizer.WinUI.Theme;

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
    private bool _isOptimizing = false;

    private readonly SolidColorBrush _cyanBrush = ThemeEffects.GetSolidBrush("Accent.Cyan");
    private readonly SolidColorBrush _violetBrush = ThemeEffects.GetSolidBrush("Accent.Purple");
    private readonly SolidColorBrush _greenBrush = ThemeEffects.GetSolidBrush("Accent.Green");
    private readonly SolidColorBrush _amberBrush = ThemeEffects.GetSolidBrush("Accent.Yellow");
    private readonly SolidColorBrush _redBrush = ThemeEffects.GetSolidBrush("State.Bad");

    public MetricItem CpuMetric { get; }
    public MetricItem GpuMetric { get; }
    public MetricItem RamMetric { get; }
    public MetricItem DiskMetric { get; }

    public ObservableCollection<LogEntry> AllLogs { get; } = new();

    public int OverallScore
    {
        get => _overallScore;
        set { _overallScore = value; OnPropertyChanged(); }
    }

    public ICommand CopyConsoleLogsCommand { get; }

    public DashboardViewModel() : this(new SystemMetricsService(), DispatcherQueue.GetForCurrentThread())
    {
    }

    public DashboardViewModel(ISystemMetricsService metricsService, DispatcherQueue? dispatcherQueue = null)
    {
        _metricsService = metricsService;
        _dispatcherQueue = dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();

        CpuMetric = new MetricItem { Name = "CPU", Icon = "💻", AccentBrush = _cyanBrush, Value = 14f };
        GpuMetric = new MetricItem { Name = "GPU", Icon = "🎮", AccentBrush = _violetBrush, Value = 18f };
        RamMetric = new MetricItem { Name = "RAM", Icon = "🧠", AccentBrush = _greenBrush, Value = 57f, SubLabel = "18.3 / 31.8 GB" };
        DiskMetric = new MetricItem { Name = "DISK", Icon = "💾", AccentBrush = _amberBrush, Value = 4f };

        for (int i = 0; i < 30; i++)
        {
            CpuMetric.History.Add(12f + (i % 6));
            GpuMetric.History.Add(15f + (i % 4));
            RamMetric.History.Add(56f + (i % 3));
            DiskMetric.History.Add(3f + (i % 5));
        }

        UpdateMetricValueBrushes();

        CopyConsoleLogsCommand = new RelayCommand(_ => CopyLogsToClipboard());

        _metricsService.SnapshotUpdated += OnSnapshotUpdated;
        _metricsService.Start();

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

    private void OnSnapshotUpdated(object? sender, SystemSnapshotEventArgs e)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            OverallScore = e.OverallScore;

            CpuMetric.Value = e.CpuPercent;
            GpuMetric.Value = e.GpuPercent;
            if (!string.IsNullOrEmpty(e.GpuSubLabel)) GpuMetric.SubLabel = e.GpuSubLabel;
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
        if (_isOptimizing) return;
        _isOptimizing = true;
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
        _isOptimizing = false;

        AddLog("OPTIMIZER", result.Message);
    }

    public async Task ExecuteQuickActionAsync(string actionId)
    {
        AddLog("OPTIMIZER", $"Triggered action '{actionId}'...");
        var result = await _metricsService.ExecuteQuickActionAsync(actionId);
        AddLog("OPTIMIZER", result.Message);
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
        });
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
