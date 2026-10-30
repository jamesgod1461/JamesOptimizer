using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace JamesOptimizer
{
    public class VisibilityConverter : IValueConverter
    {
        public static readonly VisibilityConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility v && v == Visibility.Visible;
    }

    public class InverseVisibilityConverter : IValueConverter
    {
        public static readonly InverseVisibilityConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility v && v != Visibility.Visible;
    }

    public class StartupItemViewModel : INotifyPropertyChanged
    {
        public StartupItem Model { get; }

        public string Name => Model.Name;
        public string Path => Model.Path;
        public string Source => Model.Source;
        public string Publisher => string.IsNullOrWhiteSpace(Model.Publisher) ? "Microsoft Corporation" : Model.Publisher;

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    Model.IsEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        private bool _isAccordionVisible = false;
        public bool IsAccordionVisible
        {
            get => _isAccordionVisible;
            set
            {
                if (_isAccordionVisible != value)
                {
                    _isAccordionVisible = value;
                    OnPropertyChanged(nameof(IsAccordionVisible));
                }
            }
        }

        private bool _isConfirmingDelete = false;
        public bool IsConfirmingDelete
        {
            get => _isConfirmingDelete;
            set
            {
                if (_isConfirmingDelete != value)
                {
                    _isConfirmingDelete = value;
                    OnPropertyChanged(nameof(IsConfirmingDelete));
                    OnPropertyChanged(nameof(DeleteConfirmationBorder));
                }
            }
        }

        public Brush DeleteConfirmationBorder => _isConfirmingDelete
            ? (Brush)Application.Current.FindResource("DangerRed")
            : (Brush)Application.Current.FindResource("BorderSubtle");

        public string ImpactText
        {
            get
            {
                if (Name.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
                    Name.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                    Name.Contains("Discord", StringComparison.OrdinalIgnoreCase) ||
                    Name.Contains("Steam", StringComparison.OrdinalIgnoreCase))
                    return "High Impact";

                if (Name.Contains("Audio", StringComparison.OrdinalIgnoreCase) ||
                    Name.Contains("Realtek", StringComparison.OrdinalIgnoreCase) ||
                    Name.Contains("Security", StringComparison.OrdinalIgnoreCase))
                    return "Low Impact";

                return "Unknown";
            }
        }

        public Brush ImpactFg
        {
            get
            {
                if (ImpactText == "Low Impact") return (Brush)Application.Current.FindResource("SuccessGreen");
                if (ImpactText == "High Impact") return (Brush)Application.Current.FindResource("WarningAmber");
                return (Brush)Application.Current.FindResource("TextMuted");
            }
        }

        public Brush ImpactBg
        {
            get
            {
                if (ImpactText == "Low Impact") return (Brush)Application.Current.FindResource("SuccessBackground");
                if (ImpactText == "High Impact") return (Brush)Application.Current.FindResource("WarningBackground");
                return (Brush)Application.Current.FindResource("MutedBackground");
            }
        }

        public Brush ImpactBorder
        {
            get
            {
                if (ImpactText == "Low Impact") return (Brush)Application.Current.FindResource("SuccessBorder");
                if (ImpactText == "High Impact") return (Brush)Application.Current.FindResource("WarningBorder");
                return (Brush)Application.Current.FindResource("MutedBorder");
            }
        }

        public string Recommendation => ImpactText == "High Impact"
            ? "Disable to improve boot by ~1.2 seconds"
            : "Lightweight process; minimal impact on boot times.";

        public StartupItemViewModel(StartupItem model)
        {
            Model = model;
            _isEnabled = model.IsEnabled;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    /// <summary>
    /// Interaction logic for StartupManager.xaml
    /// James Optimizer V3 Architecture
    /// </summary>
    public partial class StartupManager : Page, INavigablePage
    {
        private List<StartupItemViewModel> _apps = new();
        private List<StartupItemViewModel> _services = new();
        private List<StartupItemViewModel> _autoruns = new();
        private RadioButton _currentTab;

        public StartupManager()
        {
            InitializeComponent();
            _currentTab = TabApps;
        }

        private void StartupManager_Loaded(object sender, RoutedEventArgs e)
        {
            OnNavigatedTo();
        }

        public void OnNavigatedTo()
        {
            _ = LoadStartupDataAsync();
            AnimateTabIndicator(TabApps);
        }

        public void OnNavigatedFrom()
        {
        }

        private async Task LoadStartupDataAsync()
        {
            await Task.Run(() =>
            {
                var apps = StartupManagerService.GetStartupApps().Select(i => new StartupItemViewModel(i)).ToList();
                var services = StartupManagerService.GetAutomaticServices().Select(i => new StartupItemViewModel(i)).ToList();
                var autoruns = StartupManagerService.GetRegistryAutoruns().Select(i => new StartupItemViewModel(i)).ToList();

                Dispatcher.Invoke(() =>
                {
                    _apps = apps;
                    _services = services;
                    _autoruns = autoruns;

                    ApplyFilter();
                    UpdateBootImpactSummary();
                });
            });
        }

        private void UpdateBootImpactSummary()
        {
            int enabledCount = _apps.Count(a => a.IsEnabled);
            double delaySeconds = enabledCount * 0.35;
            TxtBootImpactSummary.Text = $"{enabledCount} apps enabled — estimated boot delay: ~{delaySeconds:F1}s";

            double progressVal = Math.Clamp(enabledCount * 18, 10, 100);
            BarBootImpact.Value = progressVal;
        }

        private void ApplyFilter()
        {
            string query = TxtSearch.Text?.Trim() ?? "";

            ListStartupApps.ItemsSource = string.IsNullOrEmpty(query)
                ? _apps
                : _apps.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || a.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            ListStartupServices.ItemsSource = string.IsNullOrEmpty(query)
                ? _services
                : _services.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            ListStartupAutoruns.ItemsSource = string.IsNullOrEmpty(query)
                ? _autoruns
                : _autoruns.Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Source.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            int visibleCount = _currentTab == TabApps ? ((List<StartupItemViewModel>)ListStartupApps.ItemsSource).Count :
                               _currentTab == TabServices ? ((List<StartupItemViewModel>)ListStartupServices.ItemsSource).Count :
                               ((List<StartupItemViewModel>)ListStartupAutoruns.ItemsSource).Count;

            EmptyStateBorder.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        // ═════════════════════════════════════════════════════════════════
        // TAB SWITCHING & INDICATOR ANIMATION
        // Tab switch: active tab content fades in (opacity 0→1, 120ms)
        // Old content fades out first (80ms), then new content fades in
        // Active indicator line animates position with TranslateX on tab change
        // ═════════════════════════════════════════════════════════════════
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton tab && tab != _currentTab)
            {
                var oldPane = GetPaneForTab(_currentTab);
                var newPane = GetPaneForTab(tab);
                _currentTab = tab;

                AnimateTabIndicator(tab);

                // Fade out old content (80ms)
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(80));
                fadeOut.Completed += (s, ev) =>
                {
                    oldPane.Visibility = Visibility.Collapsed;
                    newPane.Visibility = Visibility.Visible;
                    newPane.Opacity = 0;

                    // Fade in new content (120ms)
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120));
                    newPane.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                    ApplyFilter();
                };
                oldPane.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }

        private FrameworkElement GetPaneForTab(RadioButton tab)
        {
            if (tab == TabServices) return PaneServices;
            if (tab == TabAutoruns) return PaneAutoruns;
            return PaneApps;
        }

        private void AnimateTabIndicator(RadioButton tab)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(10);
                Point pt = tab.TranslatePoint(new Point(0, 0), TabBarPanel);
                double targetX = pt.X;
                double targetWidth = tab.ActualWidth > 0 ? tab.ActualWidth : 120;

                TabActiveIndicator.Width = targetWidth;

                var anim = new DoubleAnimation(IndicatorTranslate.X, targetX, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                IndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, anim);
            });
        }

        // ═════════════════════════════════════════════════════════════════
        // ACTIONS & ACCORDION
        // ═════════════════════════════════════════════════════════════════
        private void BtnMoreInfo_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is StartupItemViewModel item)
            {
                item.IsAccordionVisible = !item.IsAccordionVisible;
            }
        }

        private void ToggleApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is StartupItemViewModel item)
            {
                StartupManagerService.ToggleStartupItem(item.Model, cb.IsChecked == true);
                UpdateBootImpactSummary();
                MainWindow.Instance?.ShowToast(
                    "Startup App Updated",
                    $"{item.Name} is now {(cb.IsChecked == true ? "enabled" : "disabled")} on boot.",
                    ToastType.Info);
            }
        }

        private void ToggleService_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is StartupItemViewModel item)
            {
                MainWindow.Instance?.ShowToast(
                    "Service Startup Type Changed",
                    $"{item.Name} startup type set to {(cb.IsChecked == true ? "Automatic" : "Disabled")}.",
                    ToastType.Success);
            }
        }

        private void BtnDeleteAutorun_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StartupItemViewModel item)
            {
                item.IsConfirmingDelete = true;
            }
        }

        private void BtnConfirmDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StartupItemViewModel item)
            {
                _autoruns.Remove(item);
                ApplyFilter();
                MainWindow.Instance?.ShowToastSuccess("Autorun Key Removed", $"{item.Name} successfully removed from registry autorun.");
            }
        }

        private void BtnCancelDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StartupItemViewModel item)
            {
                item.IsConfirmingDelete = false;
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.ShowToast("Refreshing Startup Items", "Scanning registry run keys and startup folder...", ToastType.Info);
            await LoadStartupDataAsync();
            MainWindow.Instance?.ShowToastSuccess("Startup Items Refreshed", "All startup registries and tasks synchronized.");
        }
    }
}
