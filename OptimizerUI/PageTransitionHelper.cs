using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JamesOptimizer
{
    /// <summary>
    /// Page transition types
    /// </summary>
    public enum PageTransitionType
    {
        Fade,
        SlideLeft,
        SlideRight,
        SlideUp,
        SlideDown,
        Zoom,
        Flip
    }

    /// <summary>
    /// Helper for smooth page transitions
    /// </summary>
    public static class PageTransitionHelper
    {
        /// <summary>
        /// Transition from one page to another with animation
        /// </summary>
        public static async Task TransitionAsync(
            FrameworkElement? fromPage, 
            FrameworkElement toPage, 
            PageTransitionType type = PageTransitionType.Fade,
            TimeSpan? duration = null,
            EasingFunctionBase? easing = null)
        {
            if (fromPage == null)
            {
                // Just fade in the new page
                await FadeInAsync(toPage, duration, easing);
                return;
            }

            var animDuration = duration ?? TimeSpan.FromMilliseconds(300);
            var ease = easing ?? new QuarticEase { EasingMode = EasingMode.EaseOut };

            // Prepare new page
            toPage.Visibility = Visibility.Visible;
            toPage.Opacity = 0;
            toPage.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);

            var tasks = new List<Task>();

            // Animate out old page
            var outTask = AnimateOutAsync(fromPage, type, animDuration, ease);
            
            // Animate in new page
            var inTask = AnimateInAsync(toPage, type, animDuration, ease);

            await Task.WhenAll(outTask, inTask);

            // Hide old page completely
            fromPage.Visibility = Visibility.Collapsed;
            fromPage.Opacity = 1;
            fromPage.RenderTransform = Transform.Identity;
        }

        private static async Task AnimateOutAsync(FrameworkElement page, PageTransitionType type, TimeSpan duration, EasingFunctionBase easing)
        {
            var sb = new Storyboard();

            switch (type)
            {
                case PageTransitionType.Fade:
                    sb.Children.Add(CreateOpacityAnimation(1, 0, duration, easing));
                    break;

                case PageTransitionType.SlideLeft:
                    sb.Children.Add(CreateOpacityAnimation(1, 0, duration, easing));
                    sb.Children.Add(CreateTranslateAnimation(0, -50, duration, easing, "X"));
                    break;

                case PageTransitionType.SlideRight:
                    sb.Children.Add(CreateOpacityAnimation(1, 0, duration, easing));
                    sb.Children.Add(CreateTranslateAnimation(0, 50, duration, easing, "X"));
                    break;

                case PageTransitionType.SlideUp:
                    sb.Children.Add(CreateOpacityAnimation(1, 0, duration, easing));
                    sb.Children.Add(CreateTranslateAnimation(0, -50, duration, easing, "Y"));
                    break;

                case PageTransitionType.SlideDown:
                    sb.Children.Add(CreateOpacityAnimation(1, 0, duration, easing));
                    sb.Children.Add(CreateTranslateAnimation(0, 50, duration, easing, "Y"));
                    break;

                case PageTransitionType.Zoom:
                    sb.Children.Add(CreateOpacityAnimation(1, 0, duration, easing));
                    sb.Children.Add(CreateScaleAnimation(1, 0.9, duration, easing));
                    break;

                case PageTransitionType.Flip:
                    // Complex 3D flip - simplified to scale + fade
                    sb.Children.Add(CreateOpacityAnimation(1, 0, duration, easing));
                    sb.Children.Add(CreateScaleAnimation(1, 0.8, duration, easing));
                    break;
            }

            // Set targets
            foreach (var animation in sb.Children)
            {
                Storyboard.SetTarget(animation, page);
            }

            var tcs = new TaskCompletionSource<bool>();
            sb.Completed += (s, e) => tcs.TrySetResult(true);
            sb.Begin();

            await tcs.Task;
        }

        private static async Task AnimateInAsync(FrameworkElement page, PageTransitionType type, TimeSpan duration, EasingFunctionBase easing)
        {
            // Set initial state based on transition type
            switch (type)
            {
                case PageTransitionType.SlideLeft:
                    page.RenderTransform = new TranslateTransform(50, 0);
                    break;
                case PageTransitionType.SlideRight:
                    page.RenderTransform = new TranslateTransform(-50, 0);
                    break;
                case PageTransitionType.SlideUp:
                    page.RenderTransform = new TranslateTransform(0, 50);
                    break;
                case PageTransitionType.SlideDown:
                    page.RenderTransform = new TranslateTransform(0, -50);
                    break;
                case PageTransitionType.Zoom:
                    page.RenderTransform = new ScaleTransform(1.1, 1.1);
                    break;
                case PageTransitionType.Flip:
                    page.RenderTransform = new ScaleTransform(0.8, 0.8);
                    break;
                default: // Fade
                    page.RenderTransform = Transform.Identity;
                    break;
            }

            var sb = new Storyboard();

            switch (type)
            {
                case PageTransitionType.Fade:
                    sb.Children.Add(CreateOpacityAnimation(0, 1, duration, easing));
                    break;

                case PageTransitionType.SlideLeft:
                case PageTransitionType.SlideRight:
                case PageTransitionType.SlideUp:
                case PageTransitionType.SlideDown:
                    sb.Children.Add(CreateOpacityAnimation(0, 1, duration, easing));
                    sb.Children.Add(CreateTranslateAnimation(
                        type == PageTransitionType.SlideLeft ? 50 : 
                        type == PageTransitionType.SlideRight ? -50 : 0,
                        0, duration, easing, "X"));
                    sb.Children.Add(CreateTranslateAnimation(
                        type == PageTransitionType.SlideUp ? 50 : 
                        type == PageTransitionType.SlideDown ? -50 : 0,
                        0, duration, easing, "Y"));
                    break;

                case PageTransitionType.Zoom:
                    sb.Children.Add(CreateOpacityAnimation(0, 1, duration, easing));
                    sb.Children.Add(CreateScaleAnimation(1.1, 1, duration, easing));
                    break;

                case PageTransitionType.Flip:
                    sb.Children.Add(CreateOpacityAnimation(0, 1, duration, easing));
                    sb.Children.Add(CreateScaleAnimation(0.8, 1, duration, easing));
                    break;
            }

            foreach (var animation in sb.Children)
            {
                Storyboard.SetTarget(animation, page);
            }

            var tcs = new TaskCompletionSource<bool>();
            sb.Completed += (s, e) => 
            {
                page.RenderTransform = Transform.Identity;
                tcs.TrySetResult(true);
            };
            sb.Begin();

            await tcs.Task;
        }

        public static async Task FadeInAsync(FrameworkElement page, TimeSpan? duration, EasingFunctionBase? easing)
        {
            page.Visibility = Visibility.Visible;
            page.Opacity = 0;

            var anim = new DoubleAnimation(0, 1, duration ?? TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = easing ?? new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var tcs = new TaskCompletionSource<bool>();
            anim.Completed += (s, e) => tcs.TrySetResult(true);
            page.BeginAnimation(UIElement.OpacityProperty, anim);

            await tcs.Task;
        }

        private static DoubleAnimation CreateOpacityAnimation(double from, double to, TimeSpan duration, EasingFunctionBase easing)
        {
            return new DoubleAnimation(from, to, duration)
            {
                EasingFunction = easing
            };
        }

        private static DoubleAnimation CreateTranslateAnimation(double from, double to, TimeSpan duration, EasingFunctionBase easing, string axis)
        {
            return new DoubleAnimation(from, to, duration)
            {
                EasingFunction = easing
            };
        }

        private static DoubleAnimation CreateScaleAnimation(double from, double to, TimeSpan duration, EasingFunctionBase easing)
        {
            return new DoubleAnimation(from, to, duration)
            {
                EasingFunction = easing
            };
        }
    }

    /// <summary>
    /// Extension methods for easier page transitions
    /// </summary>
    public static class PageTransitionExtensions
    {
        /// <summary>
        /// Navigate to a new page with transition
        /// </summary>
        public static async Task NavigateToAsync(
            this Grid contentHost, 
            FrameworkElement newPage, 
            PageTransitionType type = PageTransitionType.Fade,
            TimeSpan? duration = null)
        {
            var oldPage = contentHost.Children.Cast<UIElement>()
                .FirstOrDefault(c => c.Visibility == Visibility.Visible) as FrameworkElement;

            if (oldPage != null)
            {
                await PageTransitionHelper.TransitionAsync(oldPage, newPage, type, duration);
            }
            else
            {
                newPage.Visibility = Visibility.Visible;
                await PageTransitionHelper.FadeInAsync(newPage, duration, null);
            }

            // Ensure only new page is visible
            foreach (UIElement child in contentHost.Children)
            {
                if (child != newPage && child is FrameworkElement fe)
                {
                    fe.Visibility = Visibility.Collapsed;
                }
            }
        }
    }
}