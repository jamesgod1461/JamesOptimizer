using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JamesOptimizer
{
    public partial class WalkthroughOverlay : UserControl
    {
        public event EventHandler? NextClicked;
        public event EventHandler? SkipClicked;

        public WalkthroughOverlay()
        {
            InitializeComponent();
            Loaded += WalkthroughOverlay_Loaded;
        }

        private void WalkthroughOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateOuterBounds();
            Focus(); // So we can catch Esc key
        }

        public void UpdateOuterBounds()
        {
            if (Window.GetWindow(this) is Window window)
            {
                OuterGeometry.Rect = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
            }
        }

        public void SetStepUI(WalkthroughStep step, int currentStep, int totalSteps, Rect targetRect)
        {
            Visibility = Visibility.Visible;
            Focus(); // Re-focus to keep catching Esc

            TxtWalkthroughTitle.Text = step.Title;
            TxtWalkthroughDesc.Text = step.Description;
            TxtStepCounter.Text = $"{currentStep} of {totalSteps}";

            if (currentStep == totalSteps)
            {
                BtnWalkthroughNext.Content = "Finish \u2713"; // Checkmark
            }
            else
            {
                BtnWalkthroughNext.Content = "Next \u2192";
            }

            // Expand rect slightly for padding
            double padding = 6;
            Rect paddedRect = new Rect(
                Math.Max(0, targetRect.X - padding),
                Math.Max(0, targetRect.Y - padding),
                targetRect.Width + (padding * 2),
                targetRect.Height + (padding * 2)
            );

            // Animate cutout inner bounds
            var anim = new RectAnimation
            {
                To = paddedRect,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            InnerGeometry.BeginAnimation(RectangleGeometry.RectProperty, anim);

            // Calculate Callout Position
            PositionCallout(step, paddedRect);
        }

        private void PositionCallout(WalkthroughStep step, Rect cutoutRect)
        {
            if (Window.GetWindow(this) is not Window window) return;

            CalloutCard.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double calloutWidth = CalloutCard.DesiredSize.Width;
            double calloutHeight = CalloutCard.DesiredSize.Height;
            double spacing = 15;

            double targetX = cutoutRect.X;
            double targetY = cutoutRect.Bottom + spacing;

            CalloutPosition position = step.CalloutPosition;

            // Auto-position logic
            if (position == CalloutPosition.Auto)
            {
                if (targetY + calloutHeight > window.ActualHeight && cutoutRect.Top - spacing - calloutHeight >= 0)
                {
                    position = CalloutPosition.Top;
                }
                else
                {
                    position = CalloutPosition.Bottom;
                }
            }

            switch (position)
            {
                case CalloutPosition.Top:
                    targetY = cutoutRect.Top - spacing - calloutHeight;
                    targetX = cutoutRect.Left + (cutoutRect.Width / 2) - (calloutWidth / 2);
                    break;
                case CalloutPosition.Bottom:
                    targetY = cutoutRect.Bottom + spacing;
                    targetX = cutoutRect.Left + (cutoutRect.Width / 2) - (calloutWidth / 2);
                    break;
                case CalloutPosition.Left:
                    targetX = cutoutRect.Left - spacing - calloutWidth;
                    targetY = cutoutRect.Top + (cutoutRect.Height / 2) - (calloutHeight / 2);
                    break;
                case CalloutPosition.Right:
                    targetX = cutoutRect.Right + spacing;
                    targetY = cutoutRect.Top + (cutoutRect.Height / 2) - (calloutHeight / 2);
                    break;
            }

            // Clamp to screen bounds
            targetX = Math.Max(10, Math.Min(targetX, window.ActualWidth - calloutWidth - 10));
            targetY = Math.Max(10, Math.Min(targetY, window.ActualHeight - calloutHeight - 10));

            // Animate Margin to move Callout smoothly
            var marginAnim = new ThicknessAnimation
            {
                To = new Thickness(targetX, targetY, 0, 0),
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            CalloutCard.BeginAnimation(Border.MarginProperty, marginAnim);
        }

        private void BtnWalkthroughNext_Click(object sender, RoutedEventArgs e)
        {
            NextClicked?.Invoke(this, EventArgs.Empty);
        }

        private void BtnWalkthroughSkip_Click(object sender, RoutedEventArgs e)
        {
            SkipClicked?.Invoke(this, EventArgs.Empty);
        }

        private void UserControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SkipClicked?.Invoke(this, EventArgs.Empty);
            }
        }

        private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Do nothing. Absorbs clicks outside the callout to prevent dismissing accidentally.
            e.Handled = true; 
        }
    }
}
