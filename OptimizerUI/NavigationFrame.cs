using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JamesOptimizer
{
    /// <summary>
    /// Direction of a page transition: forward slides the new page up into
    /// place, backward slides it down — giving sidebar navigation a sense of
    /// spatial hierarchy instead of one generic fade.
    /// </summary>
    public enum NavDirection
    {
        Forward,
        Backward
    }

    /// <summary>
    /// Custom Frame control for James Optimizer V3 with directional page enter/exit animations.
    /// </summary>
    public class NavigationFrame : Frame
    {
        private bool _isNavigating = false;

        public NavigationFrame()
        {
            NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden;
            Background = System.Windows.Media.Brushes.Transparent;
            Margin = new Thickness(0);
        }

        /// <summary>
        /// Replaces standard Navigate with animated page transition.
        /// </summary>
        public new bool Navigate(object source)
        {
            _ = NavigateWithAnimationAsync(source);
            return true;
        }

        /// <summary>
        /// Plays an exit animation on the current page, swaps content, then plays
        /// the mirrored enter animation on the new page. Honors the user's
        /// "animations enabled" preference by transitioning instantly when off.
        /// </summary>
        public async Task<bool> NavigateWithAnimationAsync(object source, NavDirection direction = NavDirection.Forward)
        {
            if (_isNavigating) return false;
            _isNavigating = true;

            try
            {
                // Reduced-motion preference: swap instantly, no storyboards.
                if (!ThemeManager.AnimationsEnabled)
                {
                    base.Navigate(source);
                    return true;
                }

                var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
                var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

                // Forward: old page drifts up and fades; new page rises in from below.
                // Backward: mirrored — old page sinks, new page descends from above.
                double exitY = direction == NavDirection.Forward ? -8 : 8;
                double enterY = direction == NavDirection.Forward ? 14 : -14;

                // 1. Exit animation on the current page
                if (Content is FrameworkElement currentElement)
                {
                    if (currentElement.RenderTransform is not TranslateTransform)
                    {
                        currentElement.RenderTransform = new TranslateTransform();
                    }

                    var exitSb = new Storyboard();
                    var exitOpacity = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = easeIn };
                    var exitShift = new DoubleAnimation(0, exitY, TimeSpan.FromMilliseconds(150)) { EasingFunction = easeIn };
                    Storyboard.SetTargetProperty(exitOpacity, new PropertyPath(UIElement.OpacityProperty));
                    Storyboard.SetTargetProperty(exitShift, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
                    exitSb.Children.Add(exitOpacity);
                    exitSb.Children.Add(exitShift);

                    var tcs = new TaskCompletionSource<bool>();
                    exitSb.Completed += (s, e) => tcs.TrySetResult(true);
                    exitSb.Begin(currentElement);
                    await Task.WhenAny(tcs.Task, Task.Delay(220));
                }

                // 2. Perform base navigation
                base.Navigate(source);

                // 3. Enter animation on the new page
                if (source is FrameworkElement nextElement)
                {
                    nextElement.Opacity = 0;
                    var tt = new TranslateTransform(0, enterY);
                    nextElement.RenderTransform = tt;

                    var enterSb = new Storyboard();
                    var enterOpacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(230)) { EasingFunction = easeOut };
                    var enterShift = new DoubleAnimation(enterY, 0, TimeSpan.FromMilliseconds(230)) { EasingFunction = easeOut };
                    Storyboard.SetTargetProperty(enterOpacity, new PropertyPath(UIElement.OpacityProperty));
                    Storyboard.SetTargetProperty(enterShift, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
                    enterSb.Children.Add(enterOpacity);
                    enterSb.Children.Add(enterShift);
                    enterSb.Begin(nextElement);
                }

                return true;
            }
            catch (Exception ex)
            {
                HardwareLogger.LogError("Page transition error", ex);
                return false;
            }
            finally
            {
                _isNavigating = false;
            }
        }
    }
}
