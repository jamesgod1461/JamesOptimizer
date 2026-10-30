using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace JamesOptimizer
{
    public partial class Settings : Page, INavigablePage
    {
        private bool _isInitializing = true;
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRunName = "JamesOptimizer";

        public Settings()
        {
            InitializeComponent();
        }

        private void Settings_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            try
            {
                var settings = ThemeManager.Settings;
                if (RadioThemeDark != null && RadioThemeLight != null)
                {
                    RadioThemeDark.IsChecked = ThemeManager.IsDarkTheme;
                    RadioThemeLight.IsChecked = !ThemeManager.IsDarkTheme;
                }

                if (AccentCyan != null && AccentViolet != null && AccentEmerald != null && AccentSunset != null && AccentRose != null)
                {
                    AccentCyan.IsChecked = string.Equals(settings.Accent, "cyan", StringComparison.OrdinalIgnoreCase);
                    AccentViolet.IsChecked = string.Equals(settings.Accent, "violet", StringComparison.OrdinalIgnoreCase);
                    AccentEmerald.IsChecked = string.Equals(settings.Accent, "emerald", StringComparison.OrdinalIgnoreCase);
                    AccentSunset.IsChecked = string.Equals(settings.Accent, "sunset", StringComparison.OrdinalIgnoreCase);
                    AccentRose.IsChecked = string.Equals(settings.Accent, "rose", StringComparison.OrdinalIgnoreCase);
                }

                if (BackdropAcrylic != null && BackdropMica != null && BackdropSolid != null)
                {
                    BackdropAcrylic.IsChecked = string.Equals(settings.Backdrop, "acrylic", StringComparison.OrdinalIgnoreCase);
                    BackdropMica.IsChecked = string.Equals(settings.Backdrop, "mica", StringComparison.OrdinalIgnoreCase);
                    BackdropSolid.IsChecked = string.Equals(settings.Backdrop, "none", StringComparison.OrdinalIgnoreCase);
                }

                if (ChkTray != null)
                {
                    ChkTray.IsChecked = settings.CloseToTray;
                }

                if (ChkAnimations != null)
                {
                    ChkAnimations.IsChecked = settings.AnimationsEnabled;
                }

                // Check Windows startup registry key
                if (ChkStartup != null)
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                    ChkStartup.IsChecked = key?.GetValue(AppRunName) != null;
                }
            }
            catch { }
            finally
            {
                _isInitializing = false;
            }
        }

        public void OnNavigatedTo() { }
        public void OnNavigatedFrom() { }

        private void RadioThemeDark_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ThemeManager.SetTheme(true);
        }

        private void RadioThemeLight_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ThemeManager.SetTheme(false);
        }

        private void AccentRadio_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (sender == AccentCyan) ThemeManager.SetAccent("cyan");
            else if (sender == AccentViolet) ThemeManager.SetAccent("violet");
            else if (sender == AccentEmerald) ThemeManager.SetAccent("emerald");
            else if (sender == AccentSunset) ThemeManager.SetAccent("sunset");
            else if (sender == AccentRose) ThemeManager.SetAccent("rose");

            MainWindow.Instance?.ShowToastSuccess("Accent Updated", "Dynamic accent glow applied across all controls.");
        }

        private void Backdrop_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            string backdrop = "acrylic";
            if (BackdropMica.IsChecked == true) backdrop = "mica";
            else if (BackdropSolid.IsChecked == true) backdrop = "none";

            ThemeManager.SetBackdrop(backdrop);
        }

        private void ChkStartup_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key != null)
                {
                    if (ChkStartup.IsChecked == true)
                    {
                        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue(AppRunName, $"\"{exePath}\" --minimized");
                            MainWindow.Instance?.ShowToastSuccess("Startup Enabled", "JamesOptimizer will start automatically with Windows.");
                        }
                    }
                    else
                    {
                        key.DeleteValue(AppRunName, false);
                        MainWindow.Instance?.ShowToastSuccess("Startup Disabled", "Removed from Windows startup.");
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.ShowToastError("Startup Registry", ex.Message);
            }
        }

        private void ChkTray_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ThemeManager.Settings.CloseToTray = ChkTray.IsChecked == true;
            ThemeManager.SaveSettings();
        }

        private void ChkAnimations_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ThemeManager.Settings.AnimationsEnabled = ChkAnimations.IsChecked == true;
            ThemeManager.SaveSettings();
        }

        private void BtnShowTips_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.StartTips();
        }
    }
}
