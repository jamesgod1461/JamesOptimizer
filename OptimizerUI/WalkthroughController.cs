using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace JamesOptimizer
{
    public class WalkthroughSettings
    {
        public bool HasSeenWalkthrough { get; set; } = false;
    }

    public class WalkthroughController
    {
        private readonly WalkthroughOverlay _overlay;
        private readonly Window _window;
        private List<WalkthroughStep> _steps = new();
        private int _currentIndex = 0;
        private static readonly string SettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WalkthroughSettings.json");

        public WalkthroughController(WalkthroughOverlay overlay, Window window)
        {
            _overlay = overlay;
            _window = window;
            
            _overlay.NextClicked += Overlay_NextClicked;
            _overlay.SkipClicked += Overlay_SkipClicked;
            _window.SizeChanged += Window_SizeChanged;
            _window.DpiChanged += Window_DpiChanged;
        }

        private void Window_DpiChanged(object sender, DpiChangedEventArgs e)
        {
            if (_overlay.Visibility == Visibility.Visible)
            {
                _overlay.UpdateOuterBounds();
                ShowCurrentStep();
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_overlay.Visibility == Visibility.Visible)
            {
                _overlay.UpdateOuterBounds();
                ShowCurrentStep();
            }
        }

        public void Start(List<WalkthroughStep> steps)
        {
            if (steps == null || steps.Count == 0) return;

            _steps = steps;
            _currentIndex = 0;
            _overlay.UpdateOuterBounds();
            _overlay.Visibility = Visibility.Visible;
            ShowCurrentStep();
        }

        public bool HasSeenWalkthrough()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var settings = JsonSerializer.Deserialize<WalkthroughSettings>(json);
                    return settings?.HasSeenWalkthrough ?? false;
                }
            }
            catch { }
            return false;
        }

        public void ResetWalkthrough()
        {
            MarkAsSeen(false);
        }

        private void MarkAsSeen(bool seen = true)
        {
            try
            {
                var settings = new WalkthroughSettings { HasSeenWalkthrough = seen };
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings));
            }
            catch { }
        }

        private void ShowCurrentStep()
        {
            if (_currentIndex >= _steps.Count)
            {
                EndWalkthrough();
                return;
            }

            var step = _steps[_currentIndex];

            // Execute programmatic setup hook (e.g., expanding a menu, switching a tab)
            step.OnStepEnter?.Invoke();

            // Wait for WPF layout pass to finish so TargetElement has actual width/height and visual bounds
            _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                // Verify target is valid
                if (step.TargetElement == null || !step.TargetElement.IsLoaded || !step.TargetElement.IsVisible || step.TargetElement.ActualWidth == 0 || step.TargetElement.ActualHeight == 0)
                {
                    // Degenerate bounds or collapsed element, skip gracefully
                    System.Diagnostics.Debug.WriteLine($"[Walkthrough] Skipping step '{step.Title}' because TargetElement is not visible or has 0x0 size.");
                    AdvanceStep(skipCurrentExitHook: true);
                    return;
                }

                try
                {
                    // Compute absolute coordinates relative to the Overlay/Window
                    var transform = step.TargetElement.TransformToAncestor(_overlay);
                    Rect targetRect = transform.TransformBounds(new Rect(0, 0, step.TargetElement.ActualWidth, step.TargetElement.ActualHeight));

                    _overlay.SetStepUI(step, _currentIndex + 1, _steps.Count, targetRect);
                }
                catch (Exception ex)
                {
                    // TransformToAncestor throws if element is not in the visual tree
                    System.Diagnostics.Debug.WriteLine($"[Walkthrough] Skipping step '{step.Title}' due to transform error: {ex.Message}");
                    AdvanceStep(skipCurrentExitHook: true);
                }
            }));
        }

        private void AdvanceStep(bool skipCurrentExitHook = false)
        {
            if (!skipCurrentExitHook && _currentIndex < _steps.Count)
            {
                _steps[_currentIndex].OnStepExit?.Invoke();
            }

            _currentIndex++;
            ShowCurrentStep();
        }

        private void Overlay_SkipClicked(object? sender, EventArgs e)
        {
            EndWalkthrough();
        }

        private void Overlay_NextClicked(object? sender, EventArgs e)
        {
            AdvanceStep();
        }

        public void EndWalkthrough()
        {
            // Clean up last step if any
            if (_currentIndex >= 0 && _currentIndex < _steps.Count)
            {
                _steps[_currentIndex].OnStepExit?.Invoke();
            }

            _overlay.Visibility = Visibility.Collapsed;
            MarkAsSeen(true);
        }
    }
}
