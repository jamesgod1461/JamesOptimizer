using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Windows.UI.ViewManagement;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;

namespace JamesOptimizer.WinUI.Core;

/// <summary>
/// GPU-accelerated composition spring motion utilities and global reduced-motion preference.
/// </summary>
public static class MotionHelper
{
    private const string RegistryPath = @"Software\JamesOptimizer";
    private const string ReduceAnimationsValue = "ReduceAnimations";
    private static readonly UISettings _uiSettings = new();
    private static bool? _reduceAnimationsOverride;

    // Presets: (dampingRatio, periodMs)
    public static readonly (float d, int p) Soft   = (0.68f, 60);  // cards, overlays, toasts
    public static readonly (float d, int p) Snappy = (0.50f, 50);  // elastic hover & squash-stretch press
    public static readonly (float d, int p) Pill   = (0.64f, 55);  // sliding indicators

    private static readonly Dictionary<UIElement, (DispatcherQueueTimer Timer, RoutedEventHandler UnloadedHandler)> _entranceTimers = new();
    private static readonly Dictionary<object, EventHandler<object>> _activeFrameTweens = new();

    public static bool ReduceAnimations
    {
        get
        {
            if (_reduceAnimationsOverride.HasValue)
                return _reduceAnimationsOverride.Value;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
                if (key?.GetValue(ReduceAnimationsValue) is int val)
                {
                    _reduceAnimationsOverride = val != 0;
                    return _reduceAnimationsOverride.Value;
                }
            }
            catch { }

            _reduceAnimationsOverride = false;
            return false;
        }
        set
        {
            _reduceAnimationsOverride = value;
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                key?.SetValue(ReduceAnimationsValue, value ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
            ReduceAnimationsChanged?.Invoke(null, value);
            Theme.ThemeEffects.NotifyEffectsChanged();
        }
    }

    public static event EventHandler<bool>? ReduceAnimationsChanged;

    public static bool ShouldAnimate => Theme.ThemeEffects.EffectsEnabled && _uiSettings.AnimationsEnabled;

    public static double DampedSpringCurve(double t) =>
        t >= 1.0 ? 1.0 : t <= 0.0 ? 0.0 : 1.0 - Math.Pow(1.0 - t, 3.0);

    public static void PrepareVisual(UIElement el, bool center = true)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = ElementCompositionPreview.GetElementVisual(el);
        if (center && el is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
            v.CenterPoint = new Vector3((float)fe.ActualWidth / 2f, (float)fe.ActualHeight / 2f, 0f);
    }

    public static void SpringScale(UIElement el, float target, (float d, int p) s, int durationMs = 150) =>
        SpringScaleXY(el, new Vector3(target, target, 1f), s.d, s.p);

    public static void SpringScaleXY(UIElement el, Vector3 targetScale, float dampingRatio = 0.50f, int periodMs = 50)
    {
        var v = ElementCompositionPreview.GetElementVisual(el);
        if (el is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
            v.CenterPoint = new Vector3((float)fe.ActualWidth / 2f, (float)fe.ActualHeight / 2f, 0f);

        if (!ShouldAnimate)
        {
            v.StopAnimation("Scale");
            v.Scale = targetScale;
            return;
        }

        var anim = v.Compositor.CreateSpringVector3Animation();
        anim.FinalValue = targetScale;
        anim.DampingRatio = Math.Clamp(dampingRatio, 0.25f, 1.0f);
        anim.Period = TimeSpan.FromMilliseconds(Math.Clamp(periodMs, 16, 200));
        v.StartAnimation("Scale", anim);
    }

    public static void SpringTranslate(UIElement el, float x, float y, (float d, int p) s, int durationMs = 160)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = ElementCompositionPreview.GetElementVisual(el);
        var target = new Vector3(x, y, 0f);

        if (!ShouldAnimate || durationMs <= 0)
        {
            v.StopAnimation("Translation");
            v.Properties.InsertVector3("Translation", target);
            return;
        }

        var anim = v.Compositor.CreateSpringVector3Animation();
        anim.FinalValue = target;
        anim.DampingRatio = Math.Clamp(s.d, 0.25f, 1.0f);
        anim.Period = TimeSpan.FromMilliseconds(Math.Clamp(s.p, 16, 200));
        v.StartAnimation("Translation", anim);
    }

    public static void SpringOpacity(UIElement el, float target, int ms = 150)
    {
        var v = ElementCompositionPreview.GetElementVisual(el);
        if (!ShouldAnimate || ms <= 0)
        {
            v.Opacity = target;
            return;
        }

        var ease = v.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        var a = v.Compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1f, target, ease);
        a.Duration = TimeSpan.FromMilliseconds(Math.Clamp(ms, 120, 180));
        v.StartAnimation("Opacity", a);
    }

    public static void AnimateStaggeredEntrance(IReadOnlyList<UIElement> elements, int staggerMs = 45, int durationMs = 280)
    {
        if (elements == null || elements.Count == 0) return;

        for (int i = 0; i < elements.Count; i++)
        {
            var el = elements[i];
            if (el == null) continue;

            CancelEntranceTimer(el);
            ElementCompositionPreview.SetIsTranslationEnabled(el, true);
            var visual = ElementCompositionPreview.GetElementVisual(el);

            if (!ShouldAnimate)
            {
                visual.Opacity = 1f;
                visual.Properties.InsertVector3("Translation", Vector3.Zero);
                continue;
            }

            visual.Opacity = 0f;
            visual.Properties.InsertVector3("Translation", new Vector3(0f, 16f, 0f));

            int delayMs = Math.Max(0, i * staggerMs);
            if (delayMs == 0)
            {
                SpringTranslate(el, 0f, 0f, Soft);
                SpringOpacity(el, 1f);
                continue;
            }

            var dq = el.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            if (dq == null)
            {
                SpringTranslate(el, 0f, 0f, Soft);
                SpringOpacity(el, 1f);
                continue;
            }

            var timer = dq.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(delayMs);
            timer.IsRepeating = false;

            RoutedEventHandler unloadedHandler = (_, _) => CancelEntranceTimer(el);
            if (el is FrameworkElement fe)
                fe.Unloaded += unloadedHandler;

            _entranceTimers[el] = (timer, unloadedHandler);

            timer.Tick += (_, _) =>
            {
                CancelEntranceTimer(el);
                if (el.XamlRoot == null) return;
                if (!ShouldAnimate)
                {
                    visual.Opacity = 1f;
                    visual.Properties.InsertVector3("Translation", Vector3.Zero);
                    return;
                }
                SpringTranslate(el, 0f, 0f, Soft);
                SpringOpacity(el, 1f);
            };
            timer.Start();
        }
    }

    private static void CancelEntranceTimer(UIElement el)
    {
        if (_entranceTimers.TryGetValue(el, out var entry))
        {
            entry.Timer.Stop();
            if (el is FrameworkElement fe)
                fe.Unloaded -= entry.UnloadedHandler;
            _entranceTimers.Remove(el);
        }
    }

    public static void AnimateHoverLift(UIElement element, bool pointerOver, float liftY = -3f)
    {
        if (element == null) return;
        if (!ShouldAnimate)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            var v = ElementCompositionPreview.GetElementVisual(element);
            v.Properties.InsertVector3("Translation", Vector3.Zero);
            v.Scale = Vector3.One;
            return;
        }

        SpringTranslate(element, 0f, pointerOver ? liftY : 0f, Soft);
        SpringScale(element, pointerOver ? 1.015f : 1f, Soft);
    }

    public static void AnimatePressScale(UIElement element, bool pressed)
    {
        if (element == null) return;
        if (!ShouldAnimate)
        {
            var v = ElementCompositionPreview.GetElementVisual(element);
            v.Scale = Vector3.One;
            return;
        }

        SpringScale(element, pressed ? 0.95f : 1f, Snappy);
    }

    public static void AnimateSlideY(UIElement element, float targetY, int durationMs = 160)
    {
        if (element == null) return;
        if (!ShouldAnimate || durationMs <= 0)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            ElementCompositionPreview.GetElementVisual(element)
                .Properties.InsertVector3("Translation", new Vector3(0f, targetY, 0f));
            return;
        }

        SpringTranslate(element, 0f, targetY, Pill, durationMs);
    }

    public static void AnimateSlideX(UIElement element, float targetX, int durationMs = 160)
    {
        if (element == null) return;
        if (!ShouldAnimate || durationMs <= 0)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            ElementCompositionPreview.GetElementVisual(element)
                .Properties.InsertVector3("Translation", new Vector3(targetX, 0f, 0f));
            return;
        }

        SpringTranslate(element, targetX, 0f, Pill, durationMs);
    }

    /// <summary>
    /// Wires press/release/cancel ease-out scale on a ButtonBase (handledEventsToo so ButtonBase doesn't swallow PointerPressed/Released).
    /// </summary>
    public static void AttachPressSpring(ButtonBase button)
    {
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => AnimatePressScale(button, true)), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => AnimatePressScale(button, false)), true);
        button.PointerCanceled += (_, _) => AnimatePressScale(button, false);
        button.PointerExited += (_, _) => AnimatePressScale(button, false);
        button.PointerCaptureLost += (_, _) => AnimatePressScale(button, false);
    }

    /// <summary>
    /// Runs a CompositionTarget.Rendering frame loop capped at 30 FPS and paused automatically
    /// when the window is minimized/hidden or ThemeEffects.EffectsEnabled is false.
    /// </summary>
    public static void StartFrameLoop(object key, Func<double, bool> onFrame)
    {
        StopFrameLoop(key);
        const double minFrameIntervalMs = 1000.0 / 30.0; // Mandatory 30 FPS cap
        var sw = Stopwatch.StartNew();
        double lastEvalMs = -minFrameIntervalMs;
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            if (!Theme.ThemeEffects.IsWindowVisibleAndActive)
                return;

            double elapsed = sw.Elapsed.TotalMilliseconds;
            if (elapsed - lastEvalMs < minFrameIntervalMs)
                return;
            lastEvalMs = elapsed;

            bool keepGoing = false;
            try
            {
                keepGoing = onFrame(elapsed);
            }
            finally
            {
                if (!keepGoing)
                {
                    CompositionTarget.Rendering -= handler;
                    if (_activeFrameTweens.TryGetValue(key, out var current) && ReferenceEquals(current, handler))
                        _activeFrameTweens.Remove(key);
                }
            }
        };
        _activeFrameTweens[key] = handler;
        CompositionTarget.Rendering += handler;
    }

    public static void StopFrameLoop(object key)
    {
        if (_activeFrameTweens.TryGetValue(key, out var existing))
        {
            CompositionTarget.Rendering -= existing;
            _activeFrameTweens.Remove(key);
        }
    }

    public static void StopAllFrameLoops()
    {
        foreach (var kv in _activeFrameTweens)
            CompositionTarget.Rendering -= kv.Value;
        _activeFrameTweens.Clear();
    }
}
