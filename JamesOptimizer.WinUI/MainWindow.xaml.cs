using System;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using JamesOptimizer.WinUI.Models;
using JamesOptimizer.WinUI.ViewModels;

namespace JamesOptimizer.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly OverlappedPresenter _presenter;
    private bool _isSidebarCollapsed = false;
    private bool _isConsoleExpanded = false;
    private readonly DashboardViewModel _viewModel;
    private DispatcherTimer? _consoleHeightAnimationTimer;

    public static new MainWindow? Current { get; private set; }

    public MainWindow()
    {
        Current = this;
        InitializeComponent();

        // 1. Full Bleed Custom Title Bar
        ExtendsContentIntoTitleBar = true;
        _appWindow = AppWindow;
        _presenter = (_appWindow.Presenter as OverlappedPresenter)!;

        _appWindow.Title = "JAMES OPTIMIZER PRO";
        _appWindow.Resize(new SizeInt32(1360, 860));

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = _appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.White;
            titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(40, 0, 245, 255);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(60, 0, 245, 255);
        }

        SetTitleBar(TitleBarDragArea);

        // 2. Start Pulsing Indicators
        TitlePulseStoryboard.Begin();
        ConsolePulseStoryboard.Begin();

        // 3. Connect Shared ViewModel and Terminal Stream
        _viewModel = App.ViewModel ?? new DashboardViewModel();
        TerminalLogsItemsControl.ItemsSource = _viewModel.AllLogs;
        LogCountText.Text = $"({_viewModel.AllLogs.Count} entries)";

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.CurrentConsoleMarquee))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    // Only auto-follow live logs if slider is at 100
                    if (LogScrubberSlider.Value >= 98)
                    {
                        ConsoleMarqueeText.Text = _viewModel.CurrentConsoleMarquee;
                    }
                    LogCountText.Text = $"({_viewModel.AllLogs.Count} entries)";
                });
            }
        };

        ((INotifyCollectionChanged)_viewModel.AllLogs).CollectionChanged += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LogCountText.Text = $"({_viewModel.AllLogs.Count} entries)";
                if (_isConsoleExpanded && LogScrubberSlider.Value >= 98)
                {
                    TerminalScrollViewer.ChangeView(null, 0, null, true);
                }
            });
        };

        // 4. Navigate Frame to DashboardPage
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    #region Settings Button

    private void QuickSettings_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("Settings");
    }

    #endregion

    #region Sidebar Collapse / Expand

    private void ToggleSidebarBtn_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;
        AnimateSidebar(_isSidebarCollapsed);
    }

    private void AnimateSidebar(bool collapse)
    {
        double targetWidth = collapse ? 56 : 200;

        NavHeaderLabel.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        NavLabelDashboard.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        NavLabelClean.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        NavLabelPerf.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        NavLabelGame.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        NavLabelStartup.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        NavLabelServices.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        NavLabelNet.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        NavLabelStorage.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        NavLabelToolbox.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        NavLabelDiag.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        NavLabelSettings.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        UserInfoPanel.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;

        SidebarColDef.Width = new GridLength(targetWidth);
    }

    public void NavigateTo(string tag)
    {
        var allButtons = new[]
        {
            NavDashboard, NavCleanups, NavPerformance, NavGamingMode,
            NavStartup, NavServices, NavNetwork, NavStorage,
            NavToolbox, NavDiagnostics, NavSettings
        };

        var allBars = new[]
        {
            NavDashboardBar, NavCleanBar, NavPerfBar, NavGameBar,
            NavStartupBar, NavServicesBar, NavNetBar, NavStorageBar,
            NavToolboxBar, NavDiagBar, NavSettingsBar
        };

        var allLabels = new[]
        {
            NavLabelDashboard, NavLabelClean, NavLabelPerf, NavLabelGame,
            NavLabelStartup, NavLabelServices, NavLabelNet, NavLabelStorage,
            NavLabelToolbox, NavLabelDiag, NavLabelSettings
        };

        var transparentBrush = new SolidColorBrush(Colors.Transparent);
        var elevatedBrush = (SolidColorBrush)Application.Current.Resources["BgElevatedBrush"];
        var primaryTextBrush = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"];
        var mutedTextBrush = (SolidColorBrush)Application.Current.Resources["TextMutedBrush"];

        for (int i = 0; i < allButtons.Length; i++)
        {
            allButtons[i].Background = transparentBrush;
            allBars[i].Visibility = Visibility.Collapsed;
            allLabels[i].Foreground = mutedTextBrush;
            allLabels[i].FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        }

        Type targetPageType = typeof(DashboardPage);
        int selectedIndex = 0;

        switch (tag)
        {
            case "Dashboard":
                targetPageType = typeof(DashboardPage);
                selectedIndex = 0;
                break;
            case "Cleanups":
                targetPageType = typeof(Views.CleanupsPage);
                selectedIndex = 1;
                break;
            case "Performance":
                targetPageType = typeof(Views.PerformancePage);
                selectedIndex = 2;
                break;
            case "GamingMode":
                targetPageType = typeof(Views.GamingModePage);
                selectedIndex = 3;
                break;
            case "Startup":
                targetPageType = typeof(Views.StartupPage);
                selectedIndex = 4;
                break;
            case "Services":
                targetPageType = typeof(Views.ServicesPage);
                selectedIndex = 5;
                break;
            case "Network":
                targetPageType = typeof(Views.NetworkPage);
                selectedIndex = 6;
                break;
            case "Storage":
                targetPageType = typeof(Views.StoragePage);
                selectedIndex = 7;
                break;
            case "Toolbox":
                targetPageType = typeof(Views.ToolboxPage);
                selectedIndex = 8;
                break;
            case "Diagnostics":
                targetPageType = typeof(Views.DiagnosticsPage);
                selectedIndex = 9;
                break;
            case "Settings":
                targetPageType = typeof(Views.SettingsPage);
                selectedIndex = 10;
                break;
        }

        allButtons[selectedIndex].Background = elevatedBrush;
        allBars[selectedIndex].Visibility = Visibility.Visible;
        allLabels[selectedIndex].Foreground = primaryTextBrush;
        allLabels[selectedIndex].FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;

        ContentFrame.Navigate(targetPageType, null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
        _viewModel.AddLog("SYSTEM", $"Active view shifted to: {tag.ToUpper()}");
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            ApplyButtonSpring(btn);
            NavigateTo(tag);
        }
    }

    private static void ApplyButtonSpring(Button btn)
    {
        if (btn.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform { CenterX = btn.ActualWidth / 2, CenterY = btn.ActualHeight / 2 };
            btn.RenderTransform = st;
        }
        else
        {
            st.CenterX = btn.ActualWidth / 2;
            st.CenterY = btn.ActualHeight / 2;
        }

        var animX = new DoubleAnimation { From = 0.95, To = 1.0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
        var animY = new DoubleAnimation { From = 0.95, To = 1.0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };

        var sb = new Storyboard();
        Storyboard.SetTarget(animX, st);
        Storyboard.SetTargetProperty(animX, "ScaleX");
        Storyboard.SetTarget(animY, st);
        Storyboard.SetTargetProperty(animY, "ScaleY");
        sb.Begin();
    }

    private void UserCard_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        NavigateTo("Settings");
    }

    #endregion

    #region Console Drawer (Expand / Collapse, Slider Scrubber, Clear Button)

    private void ToggleConsoleExpandBtn_Click(object sender, RoutedEventArgs e)
    {
        _isConsoleExpanded = !_isConsoleExpanded;

        double startHeight = ConsoleRowDef.Height.Value;
        double targetHeight = _isConsoleExpanded ? 220 : 36;

        if (_isConsoleExpanded)
        {
            ExpandedTerminalPanel.Visibility = Visibility.Visible;
            ConsoleExpandIcon.Text = "\uE70D"; // Chevron down
            ToolTipService.SetToolTip(ToggleConsoleExpandBtn, "Collapse Console Drawer");
        }
        else
        {
            ConsoleExpandIcon.Text = "\uE70E"; // Chevron up
            ToolTipService.SetToolTip(ToggleConsoleExpandBtn, "Expand Console Drawer");
        }

        // Mechanical smooth height transition
        _consoleHeightAnimationTimer?.Stop();
        int step = 0;
        int totalSteps = 10;
        _consoleHeightAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _consoleHeightAnimationTimer.Tick += (s, args) =>
        {
            step++;
            double progress = (double)step / totalSteps;
            // CubicEase Out
            double eased = 1.0 - Math.Pow(1.0 - progress, 3);
            double current = startHeight + ((targetHeight - startHeight) * eased);
            ConsoleRowDef.Height = new GridLength(current);

            if (step >= totalSteps)
            {
                ConsoleRowDef.Height = new GridLength(targetHeight);
                if (!_isConsoleExpanded)
                {
                    ExpandedTerminalPanel.Visibility = Visibility.Collapsed;
                }
                _consoleHeightAnimationTimer.Stop();
            }
        };
        _consoleHeightAnimationTimer.Start();
    }

    private void LogScrubberSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_viewModel == null || _viewModel.AllLogs.Count == 0) return;

        // If slider is near 100, track live latest log
        if (e.NewValue >= 98)
        {
            ConsoleMarqueeText.Text = _viewModel.CurrentConsoleMarquee;
            if (_isConsoleExpanded && TerminalScrollViewer != null)
            {
                TerminalScrollViewer.ChangeView(null, 0, null, true);
            }
        }
        else
        {
            // Scrub into historical logs (0 = oldest, 100 = newest)
            int total = _viewModel.AllLogs.Count;
            int targetIdx = (int)Math.Clamp((total - 1) - ((e.NewValue / 100.0) * (total - 1)), 0, total - 1);
            var entry = _viewModel.AllLogs[targetIdx];
            ConsoleMarqueeText.Text = $"[{entry.TimestampText}] [{entry.Category}] {entry.Message}";

            if (_isConsoleExpanded && TerminalScrollViewer != null && TerminalScrollViewer.ScrollableHeight > 0)
            {
                double scrollY = (1.0 - (e.NewValue / 100.0)) * TerminalScrollViewer.ScrollableHeight;
                TerminalScrollViewer.ChangeView(null, scrollY, null, true);
            }
        }
    }

    private void ClearLogsBtn_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ClearLogs();
        ConsoleMarqueeText.Text = "[SYSTEM] Console buffer cleared.";
        LogCountText.Text = "(0 entries)";
        LogScrubberSlider.Value = 100;
    }

    #endregion
}
