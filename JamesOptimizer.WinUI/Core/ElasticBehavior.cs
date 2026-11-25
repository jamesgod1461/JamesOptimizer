using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace JamesOptimizer.WinUI.Core;

/// <summary>
/// Attached Composition behavior providing dual DropShadow SpriteVisuals,
/// elastic spring hover/press scale & lift, and child icon/chevron nudge.
/// </summary>
public static class ElasticBehavior
{
    private sealed class ElasticState
    {
        public bool IsHooked;
        public bool IsPointerOver;
        public bool IsPressed;
        public bool IsLoaded;

        public FrameworkElement? ShadowHost;
        public ContainerVisual? ShadowRoot;
        public SpriteVisual? ContactVisual;
        public SpriteVisual? AmbientVisual;
        public DropShadow? ContactShadow;
        public DropShadow? AmbientShadow;

        public UIElement? IconChip;
        public UIElement? Chevron;

        public PointerEventHandler? PressedHandler;
        public PointerEventHandler? ReleasedHandler;
    }

    private static readonly ConditionalWeakTable<UIElement, ElasticState> _states = new();

    #region Attached DependencyProperties

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ElasticBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static readonly DependencyProperty HoverScaleProperty =
        DependencyProperty.RegisterAttached(
            "HoverScale",
            typeof(double),
            typeof(ElasticBehavior),
            new PropertyMetadata(1.04));

    public static double GetHoverScale(DependencyObject obj) => (double)obj.GetValue(HoverScaleProperty);
    public static void SetHoverScale(DependencyObject obj, double value) => obj.SetValue(HoverScaleProperty, value);

    public static readonly DependencyProperty HoverLiftYProperty =
        DependencyProperty.RegisterAttached(
            "HoverLiftY",
            typeof(double),
            typeof(ElasticBehavior),
            new PropertyMetadata(-2.0));

    public static double GetHoverLiftY(DependencyObject obj) => (double)obj.GetValue(HoverLiftYProperty);
    public static void SetHoverLiftY(DependencyObject obj, double value) => obj.SetValue(HoverLiftYProperty, value);

    public static readonly DependencyProperty ShadowIntensityProperty =
        DependencyProperty.RegisterAttached(
            "ShadowIntensity",
            typeof(double),
            typeof(ElasticBehavior),
            new PropertyMetadata(1.0));

    public static double GetShadowIntensity(DependencyObject obj) => (double)obj.GetValue(ShadowIntensityProperty);
    public static void SetShadowIntensity(DependencyObject obj, double value) => obj.SetValue(ShadowIntensityProperty, value);

    public static readonly DependencyProperty ShadowCornerRadiusProperty =
        DependencyProperty.RegisterAttached(
            "ShadowCornerRadius",
            typeof(double),
            typeof(ElasticBehavior),
            new PropertyMetadata(10.0));

    public static double GetShadowCornerRadius(DependencyObject obj) => (double)obj.GetValue(ShadowCornerRadiusProperty);
    public static void SetShadowCornerRadius(DependencyObject obj, double value) => obj.SetValue(ShadowCornerRadiusProperty, value);

    public static readonly DependencyProperty ShowShadowAtRestProperty =
        DependencyProperty.RegisterAttached(
            "ShowShadowAtRest",
            typeof(bool),
            typeof(ElasticBehavior),
            new PropertyMetadata(true));

    public static bool GetShowShadowAtRest(DependencyObject obj) => (bool)obj.GetValue(ShowShadowAtRestProperty);
    public static void SetShowShadowAtRest(DependencyObject obj, bool value) => obj.SetValue(ShowShadowAtRestProperty, value);

    public static readonly DependencyProperty NudgeChildIconsProperty =
        DependencyProperty.RegisterAttached(
            "NudgeChildIcons",
            typeof(bool),
            typeof(ElasticBehavior),
            new PropertyMetadata(true));

    public static bool GetNudgeChildIcons(DependencyObject obj) => (bool)obj.GetValue(NudgeChildIconsProperty);
    public static void SetNudgeChildIcons(DependencyObject obj, bool value) => obj.SetValue(NudgeChildIconsProperty, value);

    #endregion

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;

        bool enable = e.NewValue is true;
        var state = _states.GetOrCreateValue(el);

        if (enable && !state.IsHooked)
        {
            state.IsHooked = true;
            state.PressedHandler = OnPointerPressed;
            state.ReleasedHandler = OnPointerReleased;

            el.Loaded += OnLoaded;
            el.Unloaded += OnUnloaded;
            el.SizeChanged += OnSizeChanged;
            el.ActualThemeChanged += OnActualThemeChanged;
            el.PointerEntered += OnPointerEntered;
            el.PointerExited += OnPointerExited;
            el.PointerCanceled += OnPointerCanceled;
            el.PointerCaptureLost += OnPointerCaptureLost;
            el.AddHandler(UIElement.PointerPressedEvent, state.PressedHandler, true);
            el.AddHandler(UIElement.PointerReleasedEvent, state.ReleasedHandler, true);

            if (el is Control ctrl)
                ctrl.IsEnabledChanged += OnControlIsEnabledChanged;

            if (el.IsLoaded)
                InitializeForElement(el, state);
        }
        else if (!enable && state.IsHooked)
        {
            Unhook(el, state);
        }
    }

    private static void Unhook(FrameworkElement el, ElasticState state)
    {
        state.IsHooked = false;
        el.Loaded -= OnLoaded;
        el.Unloaded -= OnUnloaded;
        el.SizeChanged -= OnSizeChanged;
        el.ActualThemeChanged -= OnActualThemeChanged;
        el.PointerEntered -= OnPointerEntered;
        el.PointerExited -= OnPointerExited;
        el.PointerCanceled -= OnPointerCanceled;
        el.PointerCaptureLost -= OnPointerCaptureLost;

        if (state.PressedHandler != null)
            el.RemoveHandler(UIElement.PointerPressedEvent, state.PressedHandler);
        if (state.ReleasedHandler != null)
            el.RemoveHandler(UIElement.PointerReleasedEvent, state.ReleasedHandler);

        if (el is Control ctrl)
            ctrl.IsEnabledChanged -= OnControlIsEnabledChanged;

        TeardownShadows(el, state);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && _states.TryGetValue(el, out var state))
            InitializeForElement(el, state);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && _states.TryGetValue(el, out var state))
        {
            state.IsLoaded = false;
            state.IsPointerOver = false;
            state.IsPressed = false;
            TeardownShadows(el, state);
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement el && _states.TryGetValue(el, out var state))
        {
            var v = ElementCompositionPreview.GetElementVisual(el);
            if (el.ActualWidth > 0 && el.ActualHeight > 0)
                v.CenterPoint = new Vector3((float)el.ActualWidth / 2f, (float)el.ActualHeight / 2f, 0f);

            UpdateShadowSizes(el, state);
        }
    }

    private static void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_states.TryGetValue(sender, out var state))
            ApplyVisualState(sender, state, immediate: true);
    }

    private static void OnControlIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement el && _states.TryGetValue(el, out var state))
        {
            if (e.NewValue is false)
            {
                state.IsPointerOver = false;
                state.IsPressed = false;
            }
            ApplyVisualState(el, state, immediate: false);
        }
    }

    private static void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement el && _states.TryGetValue(el, out var state))
        {
            state.IsPointerOver = true;
            ApplyVisualState(el, state, immediate: false);
        }
    }

    private static void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement el && _states.TryGetValue(el, out var state))
        {
            state.IsPointerOver = false;
            state.IsPressed = false;
            ApplyVisualState(el, state, immediate: false);
        }
    }

    private static void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement el && _states.TryGetValue(el, out var state))
        {
            state.IsPressed = true;
            ApplyVisualState(el, state, immediate: false);
        }
    }

    private static void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement el && _states.TryGetValue(el, out var state))
        {
            state.IsPressed = false;
            ApplyVisualState(el, state, immediate: false);
        }
    }

    private static void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement el && _states.TryGetValue(el, out var state))
        {
            state.IsPressed = false;
            state.IsPointerOver = false;
            ApplyVisualState(el, state, immediate: false);
        }
    }

    private static void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement el && _states.TryGetValue(el, out var state))
        {
            state.IsPressed = false;
            ApplyVisualState(el, state, immediate: false);
        }
    }

    private static void InitializeForElement(FrameworkElement el, ElasticState state)
    {
        state.IsLoaded = true;
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = ElementCompositionPreview.GetElementVisual(el);
        if (el.ActualWidth > 0 && el.ActualHeight > 0)
            v.CenterPoint = new Vector3((float)el.ActualWidth / 2f, (float)el.ActualHeight / 2f, 0f);

        EnsureShadows(el, state);
        DiscoverChildTargets(el, state);
        ApplyVisualState(el, state, immediate: true);
    }

    private static void EnsureShadows(FrameworkElement el, ElasticState state)
    {
        if (state.ShadowRoot != null) return;

        var host = FindChildByName<FrameworkElement>(el, "PART_ShadowHost", maxDepth: 4);
        if (host == null) return;

        state.ShadowHost = host;
        var hostVisual = ElementCompositionPreview.GetElementVisual(host);
        var compositor = hostVisual.Compositor;

        var root = compositor.CreateContainerVisual();

        bool isLight = el.ActualTheme == ElementTheme.Light;
        var shadowColor = isLight
            ? Windows.UI.Color.FromArgb(90, 15, 23, 42)
            : Windows.UI.Color.FromArgb(230, 0, 0, 0);

        var ambient = compositor.CreateDropShadow();
        ambient.Color = shadowColor;
        ambient.Offset = new Vector3(0f, 8f, 0f);
        ambient.BlurRadius = 18f;
        ambient.Opacity = 1f;

        var ambientVisual = compositor.CreateSpriteVisual();
        ambientVisual.Shadow = ambient;

        var contact = compositor.CreateDropShadow();
        contact.Color = shadowColor;
        contact.Offset = new Vector3(0f, 2f, 0f);
        contact.BlurRadius = 4f;
        contact.Opacity = 1f;

        var contactVisual = compositor.CreateSpriteVisual();
        contactVisual.Shadow = contact;

        root.Children.InsertAtTop(ambientVisual);
        root.Children.InsertAtTop(contactVisual);

        ElementCompositionPreview.SetElementChildVisual(host, root);

        state.ShadowRoot = root;
        state.AmbientShadow = ambient;
        state.ContactShadow = contact;
        state.AmbientVisual = ambientVisual;
        state.ContactVisual = contactVisual;

        UpdateShadowSizes(el, state);
    }

    private static void UpdateShadowSizes(FrameworkElement el, ElasticState state)
    {
        if (state.ShadowHost == null || state.AmbientVisual == null || state.ContactVisual == null)
            return;

        double rawW = state.ShadowHost.ActualWidth > 0 ? state.ShadowHost.ActualWidth : el.ActualWidth;
        double rawH = state.ShadowHost.ActualHeight > 0 ? state.ShadowHost.ActualHeight : el.ActualHeight;
        if (rawW <= 4 || rawH <= 4) return;

        // Inset 2px per edge so rectangular SpriteVisual corners sit cleanly inside rounded borders
        var size = new Vector2((float)(rawW - 4.0), (float)(rawH - 4.0));
        var offset = new Vector3(2f, 2f, 0f);

        state.ShadowRoot!.Size = new Vector2((float)rawW, (float)rawH);
        state.AmbientVisual.Size = size;
        state.AmbientVisual.Offset = offset;
        state.ContactVisual.Size = size;
        state.ContactVisual.Offset = offset;
    }

    private static void TeardownShadows(FrameworkElement el, ElasticState state)
    {
        if (state.ShadowHost != null)
        {
            try { ElementCompositionPreview.SetElementChildVisual(state.ShadowHost, null); } catch { }
        }
        state.ShadowRoot?.Dispose();
        state.ContactVisual?.Dispose();
        state.AmbientVisual?.Dispose();
        state.ContactShadow?.Dispose();
        state.AmbientShadow?.Dispose();

        state.ShadowHost = null;
        state.ShadowRoot = null;
        state.ContactVisual = null;
        state.AmbientVisual = null;
        state.ContactShadow = null;
        state.AmbientShadow = null;
        state.IconChip = null;
        state.Chevron = null;
    }

    private static void DiscoverChildTargets(FrameworkElement el, ElasticState state)
    {
        if (!GetNudgeChildIcons(el)) return;

        DependencyObject searchRoot = el is ContentControl cc && cc.Content is DependencyObject contentObj
            ? contentObj
            : el;

        state.IconChip = FindIconChip(searchRoot, maxDepth: 5);
        state.Chevron = FindChevron(searchRoot, maxDepth: 5);

        if (state.IconChip != null)
            ElementCompositionPreview.SetIsTranslationEnabled(state.IconChip, true);
        if (state.Chevron != null)
            ElementCompositionPreview.SetIsTranslationEnabled(state.Chevron, true);
    }

    private static void ApplyVisualState(FrameworkElement el, ElasticState state, bool immediate)
    {
        bool controlEnabled = el is not Control c || c.IsEnabled;
        float hoverScale = (float)GetHoverScale(el);
        float hoverLiftY = (float)GetHoverLiftY(el);
        float intensity = (float)Math.Max(0.0, GetShadowIntensity(el));
        bool showAtRest = GetShowShadowAtRest(el);

        Vector3 targetScale;
        float targetLiftY;

        float contactOffsetY;
        float contactBlur;
        float contactOpacity;

        float ambientOffsetY;
        float ambientBlur;
        float ambientOpacity;

        if (!controlEnabled)
        {
            targetScale = Vector3.One;
            targetLiftY = 0f;
            contactOffsetY = 2f;
            contactBlur = 4f;
            contactOpacity = 0f;
            ambientOffsetY = 8f;
            ambientBlur = 18f;
            ambientOpacity = 0f;
        }
        else if (state.IsPressed)
        {
            targetScale = new Vector3(0.96f, 0.97f, 1f);
            targetLiftY = 0f;
            contactOffsetY = 1f;
            contactBlur = 2f;
            contactOpacity = showAtRest || state.IsPointerOver ? 0.45f * intensity : 0f;
            ambientOffsetY = 2f;
            ambientBlur = 4f;
            ambientOpacity = showAtRest || state.IsPointerOver ? 0.25f * intensity : 0f;
        }
        else if (state.IsPointerOver)
        {
            targetScale = new Vector3(hoverScale, hoverScale, 1f);
            targetLiftY = hoverLiftY;
            contactOffsetY = 4f;
            contactBlur = 8f;
            contactOpacity = 0.70f * intensity;
            ambientOffsetY = 14f;
            ambientBlur = 28f;
            ambientOpacity = 0.55f * intensity;
        }
        else
        {
            targetScale = Vector3.One;
            targetLiftY = 0f;
            contactOffsetY = 2f;
            contactBlur = 4f;
            contactOpacity = showAtRest ? 0.60f * intensity : 0f;
            ambientOffsetY = 8f;
            ambientBlur = 18f;
            ambientOpacity = showAtRest ? 0.45f * intensity : 0f;
        }

        bool animate = !immediate && MotionHelper.ShouldAnimate;

        if (!animate)
        {
            var v = ElementCompositionPreview.GetElementVisual(el);
            v.StopAnimation("Scale");
            v.StopAnimation("Translation");
            v.Scale = targetScale;
            v.Properties.InsertVector3("Translation", new Vector3(0f, targetLiftY, 0f));
        }
        else
        {
            MotionHelper.SpringScaleXY(el, targetScale, 0.50f, 50);
            MotionHelper.SpringTranslate(el, 0f, targetLiftY, MotionHelper.Snappy);
        }

        // Child icon chip & chevron nudge
        bool nudgeActive = controlEnabled && state.IsPointerOver && !state.IsPressed && GetNudgeChildIcons(el);
        if (state.IconChip != null)
        {
            if (!animate)
            {
                var iv = ElementCompositionPreview.GetElementVisual(state.IconChip);
                iv.StopAnimation("Translation");
                iv.Properties.InsertVector3("Translation", nudgeActive ? new Vector3(0f, -2f, 0f) : Vector3.Zero);
            }
            else
            {
                MotionHelper.SpringTranslate(state.IconChip, 0f, nudgeActive ? -2f : 0f, MotionHelper.Snappy);
            }
        }
        if (state.Chevron != null)
        {
            if (!animate)
            {
                var cv = ElementCompositionPreview.GetElementVisual(state.Chevron);
                cv.StopAnimation("Translation");
                cv.Properties.InsertVector3("Translation", nudgeActive ? new Vector3(3f, 0f, 0f) : Vector3.Zero);
            }
            else
            {
                MotionHelper.SpringTranslate(state.Chevron, nudgeActive ? 3f : 0f, 0f, MotionHelper.Snappy);
            }
        }

        // Dual DropShadow transition (180ms ease-out or immediate)
        if (state.ShadowHost == null && state.IsLoaded)
            EnsureShadows(el, state);

        if (state.ContactShadow != null && state.AmbientShadow != null &&
            state.ContactVisual != null && state.AmbientVisual != null)
        {
            var themeColor = el.ActualTheme == ElementTheme.Light
                ? Windows.UI.Color.FromArgb(90, 15, 23, 42)
                : Windows.UI.Color.FromArgb(230, 0, 0, 0);
            state.ContactShadow.Color = themeColor;
            state.AmbientShadow.Color = themeColor;

            contactOpacity = Math.Clamp(contactOpacity, 0f, 1f);
            ambientOpacity = Math.Clamp(ambientOpacity, 0f, 1f);

            if (!animate)
            {
                state.ContactShadow.Offset = new Vector3(0f, contactOffsetY, 0f);
                state.ContactShadow.BlurRadius = contactBlur;
                state.ContactVisual.Opacity = contactOpacity;

                state.AmbientShadow.Offset = new Vector3(0f, ambientOffsetY, 0f);
                state.AmbientShadow.BlurRadius = ambientBlur;
                state.AmbientVisual.Opacity = ambientOpacity;
            }
            else
            {
                var comp = state.ContactShadow.Compositor;
                var ease = comp.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
                var dur = TimeSpan.FromMilliseconds(180);

                AnimateVector3(state.ContactShadow, "Offset", new Vector3(0f, contactOffsetY, 0f), dur, ease);
                AnimateScalar(state.ContactShadow, "BlurRadius", contactBlur, dur, ease);
                AnimateScalar(state.ContactVisual, "Opacity", contactOpacity, dur, ease);

                AnimateVector3(state.AmbientShadow, "Offset", new Vector3(0f, ambientOffsetY, 0f), dur, ease);
                AnimateScalar(state.AmbientShadow, "BlurRadius", ambientBlur, dur, ease);
                AnimateScalar(state.AmbientVisual, "Opacity", ambientOpacity, dur, ease);
            }
        }
    }

    private static void AnimateScalar(CompositionObject target, string property, float finalValue, TimeSpan duration, CompositionEasingFunction ease)
    {
        var anim = target.Compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(1f, finalValue, ease);
        anim.Duration = duration;
        target.StartAnimation(property, anim);
    }

    private static void AnimateVector3(CompositionObject target, string property, Vector3 finalValue, TimeSpan duration, CompositionEasingFunction ease)
    {
        var anim = target.Compositor.CreateVector3KeyFrameAnimation();
        anim.InsertKeyFrame(1f, finalValue, ease);
        anim.Duration = duration;
        target.StartAnimation(property, anim);
    }

    private static T? FindChildByName<T>(DependencyObject parent, string name, int maxDepth) where T : FrameworkElement
    {
        var queue = new Queue<(DependencyObject Node, int Depth)>();
        queue.Enqueue((parent, 0));

        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (node is T fe && fe.Name == name)
                return fe;

            if (depth >= maxDepth) continue;
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                queue.Enqueue((VisualTreeHelper.GetChild(node, i), depth + 1));
        }
        return null;
    }

    private static UIElement? FindIconChip(DependencyObject root, int maxDepth)
    {
        var queue = new Queue<(DependencyObject Node, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (node is FrameworkElement fe)
            {
                if (fe.Name == "PART_IconChip") return fe;
                if (fe is Border b && Math.Abs(b.Width - 30.0) < 0.5 && Math.Abs(b.Height - 30.0) < 0.5)
                    return b;
            }
            if (depth >= maxDepth) continue;
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                queue.Enqueue((VisualTreeHelper.GetChild(node, i), depth + 1));
        }
        return null;
    }

    private static UIElement? FindChevron(DependencyObject root, int maxDepth)
    {
        var queue = new Queue<(DependencyObject Node, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (node is TextBlock tb && (tb.Name == "PART_Chevron" || tb.Text == "›"))
                return tb;

            if (depth >= maxDepth) continue;
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                queue.Enqueue((VisualTreeHelper.GetChild(node, i), depth + 1));
        }
        return null;
    }
}
