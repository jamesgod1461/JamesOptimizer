using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using JamesOptimizer;

namespace JamesOptimizer.WinUI.Views
{
    public sealed partial class StartupPage : Page
    {
        public ObservableCollection<StartupItem> StartupItems { get; } = new();

        public StartupPage()
        {
            InitializeComponent();
            StartupItemsList.ItemsSource = StartupItems;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (StartupItems.Count == 0)
            {
                _ = LoadStartupItemsAsync();
            }
        }

        private async Task LoadStartupItemsAsync()
        {
            StartupItems.Clear();
            TxtSummary.Text = "Scanning startup autorun registries and folders...";

            await Task.Run(() =>
            {
                var items = StartupManagerService.GetStartupApps();
                DispatcherQueue.TryEnqueue(() =>
                {
                    foreach (var it in items)
                    {
                        StartupItems.Add(it);
                    }

                    int activeCount = StartupItems.Count(i => i.IsEnabled);
                    TxtActiveCount.Text = $"{activeCount} Active";
                    TxtSummary.Text = $"Detected {StartupItems.Count} total startup applications.";
                });
            });
        }

        private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle && toggle.Tag is StartupItem item)
            {
                StartupManagerService.SetStartupState(item, toggle.IsOn);
                item.IsEnabled = toggle.IsOn;

                int activeCount = StartupItems.Count(i => i.IsEnabled);
                TxtActiveCount.Text = $"{activeCount} Active";

                App.ViewModel?.AddLog("STARTUP", $"Startup item '{item.Name}' set to {(toggle.IsOn ? "ENABLED" : "DISABLED")}");
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StartupItem item)
            {
                StartupManagerService.DeleteStartupItem(item);
                StartupItems.Remove(item);

                int activeCount = StartupItems.Count(i => i.IsEnabled);
                TxtActiveCount.Text = $"{activeCount} Active";
                TxtSummary.Text = $"Detected {StartupItems.Count} total startup applications.";

                App.ViewModel?.AddLog("STARTUP", $"Deleted autorun item '{item.Name}'.");
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadStartupItemsAsync();
        }
    }
}
