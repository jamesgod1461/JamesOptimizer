using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontStyle = System.Windows.FontStyle;

namespace JamesOptimizer
{
    public class ServiceItemViewModel
    {
        public string ServiceName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string Status { get; set; } = "";
        public string StartType { get; set; } = "";
        public bool IsRunning => Status.Equals("Running", StringComparison.OrdinalIgnoreCase);

        public string DotType
        {
            get
            {
                if (Publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) return "Microsoft";
                if (Publisher.StartsWith("[Verified]", StringComparison.OrdinalIgnoreCase)) return "Verified";
                return "Unverified";
            }
        }

        public string PublisherFormatted => string.IsNullOrWhiteSpace(Publisher) ? "Publisher: Unknown" : $"Publisher: {Publisher}";
        public FontStyle PublisherFontStyle => DotType == "Unverified" ? FontStyles.Italic : FontStyles.Normal;

        public string ServiceNameText => $"Service Name: {ServiceName}";
        public string StatusText => $"Status: {Status}";
        public Brush StatusBrush => IsRunning ? (Brush)Application.Current.FindResource("SuccessGreen") : (Brush)Application.Current.FindResource("DangerRed");
        public string StartupText => $"Startup: {StartType}";

        public string BadgeText => Status.ToUpperInvariant();
        public Brush BadgeBg => IsRunning ? (Brush)Application.Current.FindResource("SuccessBackground") : (Brush)Application.Current.FindResource("DangerBackground");
        public Brush BadgeBorder => IsRunning ? (Brush)Application.Current.FindResource("SuccessBorder") : (Brush)Application.Current.FindResource("DangerBorder");
        public Brush BadgeFg => IsRunning ? (Brush)Application.Current.FindResource("SuccessGreen") : (Brush)Application.Current.FindResource("DangerRed");

        public string ActionButtonText => IsRunning ? "Stop" : "Start";
        public Style ActionButtonStyle => IsRunning
            ? (Style)Application.Current.FindResource("DangerButton")
            : (Style)Application.Current.FindResource("GhostButton");
    }

    /// <summary>
    /// Interaction logic for ServiceManager.xaml
    /// James Optimizer V3 Architecture
    /// </summary>
    public partial class ServiceManager : Page, INavigablePage
    {
        private List<ServiceItemViewModel> _allServices = new();
        private bool _isLoading = false;

        public ServiceManager()
        {
            InitializeComponent();
        }

        private void ServiceManager_Loaded(object sender, RoutedEventArgs e)
        {
            OnNavigatedTo();
        }

        public void OnNavigatedTo()
        {
            _ = LoadServicesAsync();
        }

        public void OnNavigatedFrom()
        {
        }

        private async Task LoadServicesAsync()
        {
            if (_isLoading) return;
            _isLoading = true;
            LoadingIndicator.Visibility = Visibility.Visible;

            await Task.Run(() =>
            {
                var services = ServiceManagerLogic.GetAllServices();
                var viewModels = services.Select(s => new ServiceItemViewModel
                {
                    ServiceName = s.ServiceName,
                    DisplayName = string.IsNullOrWhiteSpace(s.DisplayName) ? s.ServiceName : s.DisplayName,
                    Publisher = s.Publisher,
                    Status = s.Status,
                    StartType = s.StartType
                }).ToList();

                Dispatcher.Invoke(() =>
                {
                    _allServices = viewModels;
                    ApplyFilter();
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                    _isLoading = false;
                });
            });
        }

        private void ApplyFilter()
        {
            string query = TxtSearch.Text?.Trim() ?? "";
            var filtered = string.IsNullOrEmpty(query)
                ? _allServices
                : _allServices.Where(s =>
                    s.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.ServiceName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            ServicesListControl.ItemsSource = filtered;
            EmptyStateBorder.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.ShowToast("Refreshing Services", "Scanning system service states...", ToastType.Info);
            await LoadServicesAsync();
            MainWindow.Instance?.ShowToastSuccess("Services Refreshed", $"{_allServices.Count} Windows services loaded.");
        }

        private async void BtnToggleService_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string serviceName)
            {
                var item = _allServices.FirstOrDefault(s => s.ServiceName == serviceName);
                if (item == null) return;

                bool shouldStart = !item.IsRunning;
                btn.IsEnabled = false;

                MainWindow.Instance?.ShowToast(
                    shouldStart ? "Starting Service" : "Stopping Service",
                    $"{item.DisplayName} ({item.ServiceName})...",
                    ToastType.Info);

                await Task.Run(() =>
                {
                    ServiceManagerLogic.ToggleServiceState(serviceName, shouldStart);
                });

                // Refresh after state change
                await LoadServicesAsync();
                MainWindow.Instance?.ShowToastSuccess(
                    "Service Updated",
                    $"{item.DisplayName} is now {(shouldStart ? "running" : "stopped")}.");
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // DEBLOAT MODAL CONFIRMATION
        // Modal animates in: scale 0.92→1.0 + opacity 0→1, 200ms EaseOut
        // ═════════════════════════════════════════════════════════════════
        private void BtnOpenDebloat_Click(object sender, RoutedEventArgs e)
        {
            DebloatModalGrid.Visibility = Visibility.Visible;
            DebloatModalGrid.Opacity = 0;

            var opAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleAnim = new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            DebloatModalGrid.BeginAnimation(UIElement.OpacityProperty, opAnim);
            ModalScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            ModalScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }

        private void BtnCancelDebloat_Click(object sender, RoutedEventArgs e)
        {
            DebloatModalGrid.Visibility = Visibility.Collapsed;
        }

        private async void BtnProceedDebloat_Click(object sender, RoutedEventArgs e)
        {
            DebloatModalGrid.Visibility = Visibility.Collapsed;
            MainWindow.Instance?.ShowToast("Service Debloater", "Creating System Restore Point and debloating services...", ToastType.Info);

            try
            {
                await Task.Run(async () =>
                {
                    await OptimizationEngine.CreateRestorePointAsync(CancellationToken.None);
                    await OptimizationEngine.RunServiceDebloaterAsync(CancellationToken.None);
                });

                MainWindow.Instance?.ShowToastSuccess("Debloat Completed", "Telemetry and non-essential background services disabled.");
                await LoadServicesAsync();
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Debloat Error", ex.Message);
            }
        }
    }
}
