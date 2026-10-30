using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;

namespace JamesOptimizer
{
    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class ToastNotification
    {
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public ToastType Type { get; set; } = ToastType.Info;
        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(5);
        public Action? OnClick { get; set; }
        public string? IconOverride { get; set; }
        public string? ActionText { get; set; }
        public Action? OnAction { get; set; }

        internal Border? VisualElement { get; set; }
        internal DispatcherTimer? AutoCloseTimer { get; set; }
    }

    /// <summary>
    /// V3 Toast Notification Manager:
    /// Overlaid on root grid, pointer passthrough, prepends new toasts to top,
    /// pushes existing ones down with HeightAnimation 0→target height (200ms EaseOut).
    /// </summary>
    public class ToastManager
    {
        private readonly Window _ownerWindow;
        private readonly Panel _toastContainer;
        private readonly List<ToastNotification> _activeToasts = new();
        private const int MaxToasts = 5;
        private const double ToastWidth = 340;
        private const double ToastTargetHeight = 74;

        public ToastManager(Window ownerWindow, Panel toastContainer)
        {
            _ownerWindow = ownerWindow;
            _toastContainer = toastContainer;
        }

        public void Show(string title, string message, ToastType type = ToastType.Info, TimeSpan? duration = null, Action? onClick = null)
        {
            _ownerWindow.Dispatcher.Invoke(() =>
            {
                if (_activeToasts.Count >= MaxToasts)
                {
                    RemoveToast(_activeToasts[_activeToasts.Count - 1], animate: false);
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
                _activeToasts.Insert(0, toast);

                // Prepend to top
                _toastContainer.Children.Insert(0, toast.VisualElement!);

                // Animate in: Height expansion pushes existing toasts down
                AnimateToastIn(toast);

                // Auto-close timer
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

        public void ShowSuccess(string title, string message, TimeSpan? duration = null, Action? onClick = null)
            => Show(title, message, ToastType.Success, duration, onClick);

        public void ShowWarning(string title, string message, TimeSpan? duration = null, Action? onClick = null)
            => Show(title, message, ToastType.Warning, duration, onClick);

        public void ShowError(string title, string message, TimeSpan? duration = null, Action? onClick = null)
            => Show(title, message, ToastType.Error, duration, onClick);

        public void ShowWithAction(string title, string message, string actionText, Action onAction, ToastType type = ToastType.Info, TimeSpan? duration = null)
        {
            _ownerWindow.Dispatcher.Invoke(() =>
            {
                if (_activeToasts.Count >= MaxToasts)
                {
                    RemoveToast(_activeToasts[_activeToasts.Count - 1], animate: false);
                }

                var toast = new ToastNotification
                {
                    Title = title,
                    Message = message,
                    Type = type,
                    Duration = duration ?? TimeSpan.FromSeconds(7),
                    ActionText = actionText,
                    OnAction = onAction
                };

                CreateToastVisual(toast);
                _activeToasts.Insert(0, toast);

                _toastContainer.Children.Insert(0, toast.VisualElement!);
                AnimateToastIn(toast);

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

        private void CreateToastVisual(ToastNotification toast)
        {
            var accentColor = GetAccentColor(toast.Type);
            var accentBrush = new SolidColorBrush(accentColor);
            var icon = toast.IconOverride ?? GetIcon(toast.Type);

            var border = new Border
            {
                Width = ToastWidth,
                Height = 0, // Starts at 0 for height expansion animation
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(10),
                Background = ThemeManager.GetBrush("ToastBackground") ?? new SolidColorBrush(MediaColor.FromArgb(0xEE, 0x12, 0x12, 0x1E)),
                BorderBrush = ThemeManager.GetBrush("ToastBorder") ?? new SolidColorBrush(MediaColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Opacity = 0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 2,
                    Opacity = 0.35,
                    Color = accentColor
                },
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new TranslateTransform(120, 0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) }); // Left accent bar
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });    // Icon
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });    // Close

            // Accent bar on left edge
            var accentBar = new Border
            {
                Background = accentBrush,
                CornerRadius = new CornerRadius(10, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(accentBar, 0);
            grid.Children.Add(accentBar);

            // Icon
            var iconText = new TextBlock
            {
                Text = icon,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = accentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 8, 0)
            };
            Grid.SetColumn(iconText, 1);
            grid.Children.Add(iconText);

            // Text stack
            var contentStack = new StackPanel
            {
                Margin = new Thickness(4, 10, 8, 10),
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!string.IsNullOrEmpty(toast.Title))
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = toast.Title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ThemeManager.GetBrush("ToastTitle") ?? new SolidColorBrush(MediaColor.FromRgb(0xF0, 0xF4, 0xFF)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            contentStack.Children.Add(new TextBlock
            {
                Text = toast.Message,
                FontSize = 11,
                Foreground = ThemeManager.GetBrush("ToastBody") ?? new SolidColorBrush(MediaColor.FromRgb(0x8C, 0x96, 0xB4)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, string.IsNullOrEmpty(toast.Title) ? 0 : 2, 0, 0),
                MaxHeight = 36
            });

            // Optional Interactive Action Button
            if (!string.IsNullOrEmpty(toast.ActionText) && toast.OnAction != null)
            {
                var actionBtn = new Border
                {
                    Background = new SolidColorBrush(MediaColor.FromArgb(0x28, accentColor.R, accentColor.G, accentColor.B)),
                    BorderBrush = new SolidColorBrush(accentColor),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var actionText = new TextBlock
                {
                    Text = toast.ActionText,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = accentBrush
                };
                actionBtn.Child = actionText;
                actionBtn.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    toast.OnAction?.Invoke();
                    RemoveToast(toast);
                };
                contentStack.Children.Add(actionBtn);
            }

            Grid.SetColumn(contentStack, 2);
            grid.Children.Add(contentStack);

            // Close button
            var closeBtn = new Button
            {
                Content = "✕",
                FontSize = 11,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(0x8C, 0x96, 0xB4)),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 8, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(0)
            };
            closeBtn.Click += (s, e) => RemoveToast(toast);
            Grid.SetColumn(closeBtn, 3);
            grid.Children.Add(closeBtn);

            if (toast.OnClick != null)
            {
                border.MouseLeftButtonUp += (s, e) => toast.OnClick?.Invoke();
                border.Cursor = System.Windows.Input.Cursors.Hand;
            }

            border.Child = grid;
            toast.VisualElement = border;
        }

        private void AnimateToastIn(ToastNotification toast)
        {
            if (toast.VisualElement == null) return;

            double targetHeight = !string.IsNullOrEmpty(toast.ActionText) ? 96 : ToastTargetHeight;

            // HeightAnimation 0 → target height, 200ms EaseOut (pushes lower toasts down)
            var heightAnim = new DoubleAnimation(0, targetHeight, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            toast.VisualElement.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);

            // Toast slide in (translateX 120 → 0 + opacity 0 → 1, 250ms EaseOut)
            var translateAnim = new DoubleAnimation(120, 0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            if (toast.VisualElement.RenderTransform is TranslateTransform tt)
            {
                tt.BeginAnimation(TranslateTransform.XProperty, translateAnim);
            }

            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            toast.VisualElement.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        }

        private void RemoveToast(ToastNotification toast, bool animate = true)
        {
            if (toast.VisualElement == null || !_activeToasts.Contains(toast)) return;

            _activeToasts.Remove(toast);
            toast.AutoCloseTimer?.Stop();

            if (animate)
            {
                var sb = new Storyboard();

                // Slide out to the right (translateX 0 → 120, 180ms EaseIn)
                var translateAnim = new DoubleAnimation(0, 120, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                if (toast.VisualElement.RenderTransform is TranslateTransform tt)
                {
                    Storyboard.SetTarget(translateAnim, tt);
                    Storyboard.SetTargetProperty(translateAnim, new PropertyPath(TranslateTransform.XProperty));
                    sb.Children.Add(translateAnim);
                }

                // Opacity fade out (1 → 0, 180ms)
                var opacityAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                Storyboard.SetTarget(opacityAnim, toast.VisualElement);
                Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(UIElement.OpacityProperty));
                sb.Children.Add(opacityAnim);

                // Collapse height (target height → 0, begins at 180ms, 200ms duration)
                var heightAnim = new DoubleAnimation(ToastTargetHeight, 0, TimeSpan.FromMilliseconds(200))
                {
                    BeginTime = TimeSpan.FromMilliseconds(180),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                Storyboard.SetTarget(heightAnim, toast.VisualElement);
                Storyboard.SetTargetProperty(heightAnim, new PropertyPath(FrameworkElement.HeightProperty));
                sb.Children.Add(heightAnim);

                sb.Completed += (s, e) =>
                {
                    _ownerWindow.Dispatcher.Invoke(() =>
                    {
                        _toastContainer.Children.Remove(toast.VisualElement!);
                    });
                };

                sb.Begin();
            }
            else
            {
                _toastContainer.Children.Remove(toast.VisualElement!);
            }
        }

        private MediaColor GetAccentColor(ToastType type)
        {
            return type switch
            {
                ToastType.Success => MediaColor.FromRgb(0x00, 0xFF, 0x88),
                ToastType.Warning => MediaColor.FromRgb(0xFF, 0xB8, 0x30),
                ToastType.Error   => MediaColor.FromRgb(0xFF, 0x4C, 0x6A),
                _                 => ThemeManager.GetAccentColor() // follows the user's accent theme
            };
        }

        private string GetIcon(ToastType type)
        {
            return type switch
            {
                ToastType.Success => "✓",
                ToastType.Warning => "⚠",
                ToastType.Error   => "✕",
                _                 => "ℹ"
            };
        }

        public void ClearAll()
        {
            foreach (var toast in _activeToasts.ToArray())
            {
                RemoveToast(toast);
            }
        }
    }
}