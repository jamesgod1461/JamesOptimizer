using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Shapes;

namespace JamesOptimizer
{
    /// <summary>
    /// Toast notification types
    /// </summary>
    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// A single toast notification
    /// </summary>
    public class ToastNotification
    {
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public ToastType Type { get; set; } = ToastType.Info;
        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(5);
        public Action? OnClick { get; set; }
        public string? IconOverride { get; set; }
        
        internal Border? VisualElement { get; set; }
        internal DispatcherTimer? AutoCloseTimer { get; set; }
    }

    /// <summary>
    /// Toast notification manager - handles showing, stacking, and animating toasts
    /// </summary>
    public class ToastManager
    {
        private readonly Window _ownerWindow;
        private readonly ItemsControl _toastContainer;
        private readonly List<ToastNotification> _activeToasts = new();
        private const int MaxToasts = 5;
        private const double ToastWidth = 350;
        private const double ToastSpacing = 10;

        public ToastManager(Window ownerWindow, ItemsControl toastContainer)
        {
            _ownerWindow = ownerWindow;
            _toastContainer = toastContainer;
        }

        /// <summary>
        /// Show a toast notification
        /// </summary>
        public void Show(string title, string message, ToastType type = ToastType.Info, TimeSpan? duration = null, Action? onClick = null)
        {
            _ownerWindow.Dispatcher.Invoke(() =>
            {
                // Remove oldest if at max capacity
                if (_activeToasts.Count >= MaxToasts)
                {
                    RemoveToast(_activeToasts[0], animate: false);
                }

                var toast = new ToastNotification
                {
                    Title = title,
                    Message = message,
                    Type = type,
                    Duration = duration ?? TimeSpan.FromSeconds(5),
                    OnClick = onClick
                };

                CreateToastVisual(toast);
                _activeToasts.Add(toast);
                _toastContainer.Items.Add(toast.VisualElement!);

                // Animate in
                AnimateToastIn(toast);

                // Start auto-close timer
                toast.AutoCloseTimer = new DispatcherTimer
                {
                    Interval = toast.Duration
                };
                toast.AutoCloseTimer.Tick += (s, e) =>
                {
                    toast.AutoCloseTimer?.Stop();
                    RemoveToast(toast);
                };
                toast.AutoCloseTimer.Start();
            });
        }

        /// <summary>
        /// Show a success toast
        /// </summary>
        public void ShowSuccess(string title, string message, TimeSpan? duration = null, Action? onClick = null)
            => Show(title, message, ToastType.Success, duration, onClick);

        /// <summary>
        /// Show a warning toast
        /// </summary>
        public void ShowWarning(string title, string message, TimeSpan? duration = null, Action? onClick = null)
            => Show(title, message, ToastType.Warning, duration, onClick);

        /// <summary>
        /// Show an error toast
        /// </summary>
        public void ShowError(string title, string message, TimeSpan? duration = null, Action? onClick = null)
            => Show(title, message, ToastType.Error, duration, onClick);

        /// <summary>
        /// Create the visual element for a toast
        /// </summary>
        private void CreateToastVisual(ToastNotification toast)
        {
            var accentBrush = GetAccentBrush(toast.Type);
            var icon = toast.IconOverride ?? GetIcon(toast.Type);

            var border = new Border
            {
                Width = ToastWidth,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF0, 0x1A, 0x1A, 0x2E)),
                BorderBrush = accentBrush,
                BorderThickness = new Thickness(1, 1, 1, 3),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 20,
                    ShadowDepth = 5,
                    Opacity = 0.4,
                    Color = System.Windows.Media.Colors.Black
                },
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.8, 0.8),
                Opacity = 0
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icon
            var iconText = new TextBlock
            {
                Text = icon,
                FontSize = 20,
                Foreground = accentBrush,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(15, 15, 10, 15)
            };
            Grid.SetColumn(iconText, 0);
            grid.Children.Add(iconText);

            // Content
            var contentStack = new StackPanel
            {
                Margin = new Thickness(0, 12, 15, 12),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(contentStack, 1);
            
            if (!string.IsNullOrEmpty(toast.Title))
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = toast.Title,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            
            contentStack.Children.Add(new TextBlock
            {
                Text = toast.Message,
                FontSize = 12,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB0, 0xB3, 0xBA)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, string.IsNullOrEmpty(toast.Title) ? 0 : 4, 0, 0)
            });
            grid.Children.Add(contentStack);

            // Close button
            var closeBtn = new Button
            {
                Content = "✕",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7A, 0x7D, 0x85)),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 10, 10, 10),
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(0)
            };
            closeBtn.Click += (s, e) => RemoveToast(toast);
            Grid.SetColumn(closeBtn, 2);
            grid.Children.Add(closeBtn);

            // Click handler for custom action
            if (toast.OnClick != null)
            {
                border.MouseLeftButtonUp += (s, e) => toast.OnClick?.Invoke();
                border.Cursor = System.Windows.Input.Cursors.Hand;
            }

            // Progress bar for auto-close
            var progressBar = new Border
            {
                Height = 3,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = accentBrush,
                CornerRadius = new CornerRadius(0, 0, 11, 11),
                RenderTransformOrigin = new System.Windows.Point(0, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            grid.Children.Add(progressBar);

            // Animate progress bar
            var progressAnim = new DoubleAnimation(1, 0, toast.Duration)
            {
                EasingFunction = null // Linear interpolation (no easing)
            };
            progressAnim.Completed += (s, e) => { /* Auto-close handled by timer */ };
            progressBar.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, progressAnim);

            border.Child = grid;
            toast.VisualElement = border;
        }

        private void AnimateToastIn(ToastNotification toast)
        {
            if (toast.VisualElement == null) return;

            var sb = new Storyboard();

            // Scale animation
            var scaleX = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
            };
            var scaleY = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(scaleX, toast.VisualElement);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            Storyboard.SetTarget(scaleY, toast.VisualElement);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

            // Opacity animation
            var opacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(opacity, toast.VisualElement);
            Storyboard.SetTargetProperty(opacity, new PropertyPath(UIElement.OpacityProperty));

            // Slide in from right
            var translateX = new DoubleAnimation(50, 0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut }
            };
            var translateTransform = new TranslateTransform();
            toast.VisualElement.RenderTransform = new TransformGroup
            {
                Children = new TransformCollection 
                { 
                    new ScaleTransform(1, 1), 
                    translateTransform 
                }
            };
            Storyboard.SetTarget(translateX, translateTransform);
            Storyboard.SetTargetProperty(translateX, new PropertyPath(TranslateTransform.XProperty));

            sb.Children.Add(scaleX);
            sb.Children.Add(scaleY);
            sb.Children.Add(opacity);
            sb.Children.Add(translateX);

            sb.Begin();
        }

        private void RemoveToast(ToastNotification toast, bool animate = true)
        {
            if (toast.VisualElement == null || !_activeToasts.Contains(toast)) return;

            _activeToasts.Remove(toast);
            toast.AutoCloseTimer?.Stop();

            if (animate)
            {
                var sb = new Storyboard();

                var scaleX = new DoubleAnimation(1.0, 0.8, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                var scaleY = new DoubleAnimation(1.0, 0.8, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                var opacity = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                var translateX = new DoubleAnimation(0, 50, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };

                if (toast.VisualElement.RenderTransform is TransformGroup tg)
                {
                    var scaleTransform = tg.Children[0] as ScaleTransform;
                    var translateTransform = tg.Children[1] as TranslateTransform;

                    if (scaleTransform != null)
                    {
                        Storyboard.SetTarget(scaleX, scaleTransform);
                        Storyboard.SetTargetProperty(scaleX, new PropertyPath(ScaleTransform.ScaleXProperty));
                        Storyboard.SetTarget(scaleY, scaleTransform);
                        Storyboard.SetTargetProperty(scaleY, new PropertyPath(ScaleTransform.ScaleYProperty));
                    }

                    if (translateTransform != null)
                    {
                        Storyboard.SetTarget(translateX, translateTransform);
                        Storyboard.SetTargetProperty(translateX, new PropertyPath(TranslateTransform.XProperty));
                    }
                }

                Storyboard.SetTarget(opacity, toast.VisualElement);
                Storyboard.SetTargetProperty(opacity, new PropertyPath(UIElement.OpacityProperty));

                sb.Completed += (s, e) =>
                {
_ownerWindow.Dispatcher.Invoke(() =>
                    {
                        _toastContainer.Items.Remove(toast.VisualElement!);
                        RepositionToasts();
                    });
                };

                sb.Children.Add(scaleX);
                sb.Children.Add(scaleY);
                sb.Children.Add(opacity);
                sb.Children.Add(translateX);
                sb.Begin();
            }
            else
            {
                _toastContainer.Items.Remove(toast.VisualElement!);
                RepositionToasts();
            }
        }

        private void RepositionToasts()
        {
            // Toasts are stacked vertically in the container, so they naturally reposition
            // when one is removed. If using Canvas, we'd animate Y positions here.
        }

        private SolidColorBrush GetAccentBrush(ToastType type)
        {
            return type switch
            {
                ToastType.Success => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81)),
                ToastType.Warning => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B)),
                ToastType.Error => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)),
                _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xD2, 0xFF))
            };
        }

        private string GetIcon(ToastType type)
        {
            return type switch
            {
                ToastType.Success => "✓",
                ToastType.Warning => "⚠",
                ToastType.Error => "✕",
                _ => "ℹ"
            };
        }

        /// <summary>
        /// Clear all toasts
        /// </summary>
        public void ClearAll()
        {
            foreach (var toast in _activeToasts.ToArray())
            {
                RemoveToast(toast);
            }
        }
    }
}