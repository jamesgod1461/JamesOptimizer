using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JamesOptimizer.WinUI.Views;

public sealed partial class StartupPage : Page
{
    private List<StartupItem> _allItems = new();
    public ObservableCollection<StartupItem> StartupItems { get; } = new();
    private string _activeScope = "App";

    public StartupPage()
    {
        InitializeComponent();
        StartupItemsList.ItemsSource = StartupItems;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_allItems.Count == 0)
            _ = LoadStartupItemsAsync();
    }

    private async Task LoadStartupItemsAsync()
    {
        StartupItems.Clear();
        TxtSummary.Text = "Scanning Startup Apps, Scheduled Tasks, Registry Autoruns & 3rd-party services...";

        var combined = await Task.Run(() =>
        {
            var list = new List<StartupItem>();
            list.AddRange(StartupManagerService.GetStartupApps());
            list.AddRange(StartupManagerService.GetScheduledTasks());
            list.AddRange(StartupManagerService.GetRegistryAutoruns());
            list.AddRange(StartupManagerService.GetThirdPartyServices());
            return list;
        });

        _allItems = combined;
        ApplyStartupFilter();
    }

    private void ScopeTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string scope) return;
        _activeScope = scope;

        var active = (Style)Application.Current.Resources["SegmentedPillActiveButtonStyle"];
        var normal = (Style)Application.Current.Resources["SegmentedPillButtonStyle"];
        TabApps.Style = scope == "App" ? active : normal;
        TabTasks.Style = scope == "ScheduledTask" ? active : normal;
        TabAutoruns.Style = scope == "Autorun" ? active : normal;
        TabServices.Style = scope == "Service" ? active : normal;
        TabAll.Style = scope == "ALL" ? active : normal;

        ApplyStartupFilter();
    }

    private void ApplyStartupFilter()
    {
        string query = (TxtStartupSearch?.Text ?? "").Trim();
        StartupItems.Clear();
        foreach (var it in _allItems)
        {
            bool scopeMatch = _activeScope == "ALL" || it.Type.Equals(_activeScope, StringComparison.OrdinalIgnoreCase);
            bool textMatch = string.IsNullOrEmpty(query) ||
                             it.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                             it.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                             it.Source.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (scopeMatch && textMatch)
                StartupItems.Add(it);
        }
        UpdateCounters();
    }

    private void UpdateCounters()
    {
        int activeCount = _allItems.Count(i => i.IsEnabled);
        double savedSec = _allItems.Where(i => !i.IsEnabled).Sum(i => i.EstimatedBootSeconds);
        TxtActiveCount.Text = $"{activeCount} Active";
        TxtBootSavings.Text = $"Est. Saved: {savedSec:F1}s";
        TxtSummary.Text = $"Showing {StartupItems.Count} of {_allItems.Count} total autorun & service entries.";
    }

    private void TxtStartupSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyStartupFilter();

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.Tag is StartupItem item)
        {
            if (!toggle.IsLoaded || item.IsEnabled == toggle.IsOn) return;

            bool desired = toggle.IsOn;
            if (!StartupManagerService.SetStartupState(item, desired))
            {
                toggle.IsOn = item.IsEnabled;
                MainWindow.Current?.ShowToast("Startup Update Failed", $"Could not update '{item.Name}' (protected or access denied).", "State.Bad");
                return;
            }
            UpdateCounters();

            App.ViewModel?.AddLog("STARTUP", $"{item.Type} '{item.Name}' set to {(desired ? "ENABLED" : "DISABLED")}");
            MainWindow.Current?.ShowToast("Startup Updated", $"{item.Name} {(desired ? "enabled" : "disabled")}.", desired ? "Accent.Cyan" : "State.Good");
        }
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is StartupItem item)
        {
            StartupManagerService.DeleteStartupItem(item);
            _allItems.Remove(item);
            StartupItems.Remove(item);
            UpdateCounters();

            App.ViewModel?.AddLog("STARTUP", $"Removed startup item '{item.Name}'.");
            MainWindow.Current?.ShowToast("Autorun Removed", $"{item.Name} removed.", "State.Good");
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) =>
        await LoadStartupItemsAsync();
}
