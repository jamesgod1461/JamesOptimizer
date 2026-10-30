using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using JamesOptimizer;

namespace JamesOptimizer.WinUI.Views
{
    public class ServiceDisplayItem : INotifyPropertyChanged
    {
        private string _status = "Stopped";

        public string ServiceName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string PathName { get; set; } = "";
        public string StartType { get; set; } = "";
        public bool IsTelemetry { get; set; }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }

        public string ActionButtonText => Status == "Running" ? "Stop" : "Start";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? prop = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public sealed partial class ServicesPage : Page
    {
        private readonly List<ServiceDisplayItem> _allServices = new();
        public ObservableCollection<ServiceDisplayItem> FilteredServices { get; } = new();
        private string _currentFilter = "ALL";

        private static readonly HashSet<string> TelemetryServiceNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "DiagTrack", "dmwappushservice", "diagnosticshub.standardcollector.service",
            "MapsBroker", "RetailDemo", "lfsvc", "SensorService", "SensrSvc"
        };

        public ServicesPage()
        {
            InitializeComponent();
            ServicesList.ItemsSource = FilteredServices;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_allServices.Count == 0)
            {
                _ = LoadServicesAsync();
            }
        }

        private async Task LoadServicesAsync()
        {
            _allServices.Clear();
            FilteredServices.Clear();
            TxtCountStatus.Text = "Loading Windows services...";

            await Task.Run(() =>
            {
                var services = ServiceManagerLogic.GetAllServices();
                foreach (var s in services)
                {
                    _allServices.Add(new ServiceDisplayItem
                    {
                        ServiceName = s.ServiceName,
                        DisplayName = string.IsNullOrEmpty(s.DisplayName) ? s.ServiceName : s.DisplayName,
                        PathName = s.PathName,
                        StartType = s.StartType,
                        Status = s.Status,
                        IsTelemetry = TelemetryServiceNames.Contains(s.ServiceName)
                    });
                }
            });

            ApplyFilter();
            TxtCountStatus.Text = $"Showing {FilteredServices.Count} of {_allServices.Count} services";
        }

        private void ApplyFilter()
        {
            FilteredServices.Clear();
            IEnumerable<ServiceDisplayItem> query = _allServices;

            switch (_currentFilter)
            {
                case "TELEMETRY":
                    query = _allServices.Where(s => s.IsTelemetry);
                    break;
                case "RUNNING":
                    query = _allServices.Where(s => s.Status == "Running");
                    break;
                case "STOPPED":
                    query = _allServices.Where(s => s.Status != "Running");
                    break;
            }

            foreach (var item in query.OrderBy(s => s.DisplayName))
            {
                FilteredServices.Add(item);
            }

            TxtCountStatus.Text = $"Showing {FilteredServices.Count} of {_allServices.Count} services";
        }

        private void Filter_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                _currentFilter = tag;
                ApplyFilter();
            }
        }

        private async void BtnToggleService_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ServiceDisplayItem item)
            {
                btn.IsEnabled = false;

                await Task.Run(() =>
                {
                    if (item.Status == "Running")
                    {
                        ServiceManagerLogic.StopService(item.ServiceName);
                        item.Status = "Stopped";
                    }
                    else
                    {
                        ServiceManagerLogic.StartService(item.ServiceName);
                        item.Status = "Running";
                    }
                });

                btn.IsEnabled = true;
                App.ViewModel?.AddLog("SERVICE", $"Service '{item.ServiceName}' transitioned to {item.Status}.");
            }
        }

        private async void BtnDisableTelemetryServices_Click(object sender, RoutedEventArgs e)
        {
            BtnDisableTelemetryServices.IsEnabled = false;
            App.ViewModel?.AddLog("OPTIMIZER", "Disabling unneeded Windows telemetry & diagnostic services...");

            await Task.Run(() =>
            {
                foreach (var name in TelemetryServiceNames)
                {
                    try
                    {
                        ServiceManagerLogic.StopService(name);
                        ServiceManagerLogic.ChangeStartType(name, "Disabled");
                    }
                    catch { }
                }
            });

            await LoadServicesAsync();
            BtnDisableTelemetryServices.IsEnabled = true;
            App.ViewModel?.AddLog("OPTIMIZER", "Telemetry services disabled and set to manual/disabled.");
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadServicesAsync();
        }
    }
}
