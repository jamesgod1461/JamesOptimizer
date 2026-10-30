using System;
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
    public class PerformanceTweakItem : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string IconGlyph { get; set; } = "\uE770";
        public bool IsRecommended { get; set; } = true;
        public Visibility IsRecommendedVisibility => IsRecommended ? Visibility.Visible : Visibility.Collapsed;

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public Action<bool>? ApplyAction { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? prop = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public sealed partial class PerformancePage : Page
    {
        public ObservableCollection<PerformanceTweakItem> Tweaks { get; } = new();

        public PerformancePage()
        {
            InitializeComponent();
            TweaksList.ItemsSource = Tweaks;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (Tweaks.Count == 0)
            {
                InitTweaks();
            }
        }

        private void InitTweaks()
        {
            Tweaks.Clear();

            Tweaks.Add(new PerformanceTweakItem
            {
                Id = "power",
                Title = "Ultimate Performance Power Plan",
                Description = "Unlocks Windows highest power plan for maximum CPU boost clocks and zero core downthrottling.",
                IconGlyph = "\uE7E8",
                IsRecommended = true,
                IsEnabled = false,
                ApplyAction = enabled =>
                {
                    if (enabled) _ = OptimizationEngine.EnableUltimatePowerPlanAsync();
                }
            });

            Tweaks.Add(new PerformanceTweakItem
            {
                Id = "telemetry",
                Title = "Disable Windows Telemetry & Diagnostics",
                Description = "Disables DiagTrack, dmwappushservice, CEIP, and background diagnostic logging.",
                IconGlyph = "\uE72E",
                IsRecommended = true,
                IsEnabled = true,
                ApplyAction = enabled =>
                {
                    if (enabled) _ = OptimizationEngine.DisableTelemetryAsync();
                }
            });

            Tweaks.Add(new PerformanceTweakItem
            {
                Id = "visuals",
                Title = "Optimize Visual Effects & Smoothness",
                Description = "Adjusts Windows visual effects to eliminate UI latency and window rendering overhead.",
                IconGlyph = "\uE790",
                IsRecommended = true,
                IsEnabled = true,
                ApplyAction = enabled =>
                {
                    if (enabled) _ = OptimizationEngine.OptimizeVisualEffectsAsync();
                }
            });

            Tweaks.Add(new PerformanceTweakItem
            {
                Id = "network_throttling",
                Title = "Disable Network Throttling Index",
                Description = "Sets NetworkThrottlingIndex to maximum (0xFFFFFFFF) to prevent packet delays while gaming.",
                IconGlyph = "\uE839",
                IsRecommended = true,
                IsEnabled = true,
                ApplyAction = enabled =>
                {
                    if (enabled) _ = OptimizationEngine.DisableNetworkThrottlingAsync();
                }
            });

            Tweaks.Add(new PerformanceTweakItem
            {
                Id = "core_parking",
                Title = "Disable CPU Core Parking",
                Description = "Keeps CPU cores awake and eliminates thread migration stutter during heavy multitasking.",
                IconGlyph = "\uE950",
                IsRecommended = true,
                IsEnabled = true,
                ApplyAction = enabled =>
                {
                    if (enabled) _ = OptimizationEngine.DisableCoreParkingAsync();
                }
            });

            Tweaks.Add(new PerformanceTweakItem
            {
                Id = "system_responsiveness",
                Title = "Optimize System Responsiveness (Multimedia QoS)",
                Description = "Sets SystemResponsiveness to 0 in Multimedia SystemProfile for raw gaming frame delivery.",
                IconGlyph = "\uE9D9",
                IsRecommended = true,
                IsEnabled = true,
                ApplyAction = enabled =>
                {
                    if (enabled) _ = OptimizationEngine.OptimizeSystemResponsivenessAsync();
                }
            });

            Tweaks.Add(new PerformanceTweakItem
            {
                Id = "indexing",
                Title = "Disable Windows Search Indexer",
                Description = "Stops background disk indexing scans on SSD drives to reduce wear and background write spikes.",
                IconGlyph = "\uE721",
                IsRecommended = false,
                IsEnabled = false,
                ApplyAction = enabled =>
                {
                    if (enabled) _ = OptimizationEngine.DisableSearchIndexingAsync();
                }
            });

            Tweaks.Add(new PerformanceTweakItem
            {
                Id = "cortana",
                Title = "Disable Background Cortana & Bing Search",
                Description = "Removes web search queries and background assistants from Windows Explorer and Start menu.",
                IconGlyph = "\uE71D",
                IsRecommended = true,
                IsEnabled = true,
                ApplyAction = enabled =>
                {
                    if (enabled) _ = OptimizationEngine.DisableCortanaAndBingAsync();
                }
            });
        }

        private void TweakToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle && toggle.Tag is string id)
            {
                var tweak = Tweaks.FirstOrDefault(t => t.Id == id);
                if (tweak != null)
                {
                    tweak.ApplyAction?.Invoke(toggle.IsOn);
                    App.ViewModel?.AddLog("TWEAK", $"Tweak '{tweak.Title}' -> {(toggle.IsOn ? "ENABLED" : "DISABLED")}");
                }
            }
        }

        private async void BtnApplyAll_Click(object sender, RoutedEventArgs e)
        {
            BtnApplyAll.IsEnabled = false;
            App.ViewModel?.AddLog("OPTIMIZER", "Applying all recommended performance tweaks...");

            await Task.Run(async () =>
            {
                foreach (var tweak in Tweaks.Where(t => t.IsRecommended))
                {
                    tweak.ApplyAction?.Invoke(true);
                    DispatcherQueue.TryEnqueue(() => tweak.IsEnabled = true);
                    await Task.Delay(150);
                }
            });

            BtnApplyAll.IsEnabled = true;
            App.ViewModel?.AddLog("OPTIMIZER", "All recommended performance tweaks applied successfully!");
        }
    }
}
