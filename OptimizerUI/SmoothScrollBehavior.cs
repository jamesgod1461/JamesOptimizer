using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace JamesOptimizer
{
    public static class SmoothScrollBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(SmoothScrollBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Window window)
            {
                if ((bool)e.NewValue)
                {
                    window.PreviewMouseWheel += Window_PreviewMouseWheel;
                }
                else
                {
                    window.PreviewMouseWheel -= Window_PreviewMouseWheel;
                }
            }
        }

        // Keep track of active scrolling animations
        private static readonly Dictionary<ScrollViewer, ScrollAnimationData> ActiveAnimations = new Dictionary<ScrollViewer, ScrollAnimationData>();
        private static bool isRendering = false;

        private class ScrollAnimationData
        {
            public double TargetOffset { get; set; }
            public double Velocity { get; set; }
        }

        private static void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = GetScrollViewerUnderMouse();
            if (scrollViewer == null) return;
            
            // If the scroll viewer doesn't need to scroll, let it bubble up
            if (scrollViewer.ScrollableHeight <= 0) return;

            e.Handled = true;

            // Get or create animation data for this ScrollViewer
            if (!ActiveAnimations.TryGetValue(scrollViewer, out var data))
            {
                data = new ScrollAnimationData { TargetOffset = scrollViewer.VerticalOffset };
                ActiveAnimations[scrollViewer] = data;
            }

            // Update the target offset based on the mouse wheel delta (adjust multiplier for sensitivity)
            data.TargetOffset -= e.Delta * 0.7;

            // Clamp the target offset
            if (data.TargetOffset < 0) data.TargetOffset = 0;
            if (data.TargetOffset > scrollViewer.ScrollableHeight) data.TargetOffset = scrollViewer.ScrollableHeight;

            // Start the render loop if it's not running
            if (!isRendering)
            {
                CompositionTarget.Rendering += CompositionTarget_Rendering;
                isRendering = true;
            }
        }

        private static void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            var completedAnimations = new List<ScrollViewer>();

            foreach (var kvp in ActiveAnimations)
            {
                var scrollViewer = kvp.Key;
                var data = kvp.Value;

                double currentOffset = scrollViewer.VerticalOffset;
                double targetOffset = data.TargetOffset;

                // Simple Lerp (Linear Interpolation) for exponential decay smooth scrolling
                double difference = targetOffset - currentOffset;
                
                // If we're very close to the target, snap to it and mark as completed
                if (Math.Abs(difference) < 0.5)
                {
                    scrollViewer.ScrollToVerticalOffset(targetOffset);
                    completedAnimations.Add(scrollViewer);
                }
                else
                {
                    // Move 20% of the distance each frame for a smooth, easing effect
                    double step = difference * 0.2;
                    scrollViewer.ScrollToVerticalOffset(currentOffset + step);
                }
            }

            // Remove completed animations
            foreach (var scrollViewer in completedAnimations)
            {
                ActiveAnimations.Remove(scrollViewer);
            }

            // Stop the render loop if there's nothing to animate
            if (ActiveAnimations.Count == 0)
            {
                CompositionTarget.Rendering -= CompositionTarget_Rendering;
                isRendering = false;
            }
        }

        private static ScrollViewer? GetScrollViewerUnderMouse()
        {
            var element = Mouse.DirectlyOver as DependencyObject;
            while (element != null)
            {
                if (element is ScrollViewer sv && sv.ComputedVerticalScrollBarVisibility == Visibility.Visible)
                {
                    return sv;
                }
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }
    }
}

