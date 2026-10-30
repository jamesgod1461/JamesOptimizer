using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace JamesOptimizer.WinUI.Models;

public class MetricItem : INotifyPropertyChanged
{
    private float _value;
    private string _displayValue = "0%";
    private string _subLabel = string.Empty;
    private SolidColorBrush _valueBrush = new(Microsoft.UI.Colors.Transparent);
    private SolidColorBrush _accentBrush = new(Microsoft.UI.Colors.Transparent);

    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;

    public float Value
    {
        get => _value;
        set
        {
            if (Math.Abs(_value - value) > 0.01f)
            {
                _value = value;
                OnPropertyChanged();
                DisplayValue = $"{Math.Round(value)}%";
            }
        }
    }

    public string DisplayValue
    {
        get => _displayValue;
        set
        {
            if (_displayValue != value)
            {
                _displayValue = value;
                OnPropertyChanged();
            }
        }
    }

    public string SubLabel
    {
        get => _subLabel;
        set
        {
            if (_subLabel != value)
            {
                _subLabel = value;
                OnPropertyChanged();
            }
        }
    }

    public SolidColorBrush ValueBrush
    {
        get => _valueBrush;
        set
        {
            if (_valueBrush != value)
            {
                _valueBrush = value;
                OnPropertyChanged();
            }
        }
    }

    public SolidColorBrush AccentBrush
    {
        get => _accentBrush;
        set
        {
            if (_accentBrush != value)
            {
                _accentBrush = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<float> History { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class DriveMetric : INotifyPropertyChanged
{
    private double _usedGb;
    private double _totalGb;
    private double _freeGb;
    private double _usageRatio;
    private string _usageSummary = string.Empty;
    private SolidColorBrush _fillBrush = new(Microsoft.UI.Colors.Transparent);

    public string DriveLetter { get; set; } = "C:";
    public string VolumeLabel { get; set; } = "System NVMe";

    public double UsedGb
    {
        get => _usedGb;
        set { _usedGb = value; OnPropertyChanged(); }
    }

    public double TotalGb
    {
        get => _totalGb;
        set { _totalGb = value; OnPropertyChanged(); }
    }

    public double FreeGb
    {
        get => _freeGb;
        set { _freeGb = value; OnPropertyChanged(); }
    }

    public double UsageRatio
    {
        get => _usageRatio;
        set { _usageRatio = value; OnPropertyChanged(); }
    }

    public string UsageSummary
    {
        get => _usageSummary;
        set { _usageSummary = value; OnPropertyChanged(); }
    }

    public SolidColorBrush FillBrush
    {
        get => _fillBrush;
        set { _fillBrush = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class SubMetricBadge
{
    public string Name { get; set; } = string.Empty;
    public int Percentage { get; set; }
    public string PercentageText => $"{Percentage}%";
    public SolidColorBrush AccentBrush { get; set; } = new(Microsoft.UI.Colors.Transparent);
}

public class QuickActionItem
{
    public string Id { get; set; } = string.Empty;
    public string Glyph { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public SolidColorBrush AccentBrush { get; set; } = new(Microsoft.UI.Colors.Transparent);
    public SolidColorBrush TintBrush { get; set; } = new(Microsoft.UI.Colors.Transparent);
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string TimestampText => Timestamp.ToString("HH:mm:ss");
    public string Category { get; set; } = "SYSTEM";
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }

    public string FormattedText => $"[{TimestampText}] [{Category}] {Message}";
}
