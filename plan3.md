# Plan 3: Motion Design: Elastic, Interactive, Alive

**Project:** JamesOptimizer V3 (WinUI 3, C#)
**Goal:** Replace flat clamped-bezier motion with real GPU-composited springs, fix broken/snapped transitions, and make every dashboard surface respond physically to input, with near-zero idle cost.

**Files touched:** `MotionHelper.cs`, `MainWindow.xaml(.cs)`, `DashboardPage.xaml(.cs)`, `Styles/Controls.xaml`

## Execution Rules (read first)

1. Work phase by phase, in order. Build (`dotnet build`) after every phase; fix errors before moving on.
2. Use **Composition animations** (`Compositor`, `Visual`) for Translation, Scale, Offset, Opacity. Do **not** animate layout properties (Width/Height) on the UI thread when a Scale/Offset can do the job. The only exceptions are listed explicitly below.
3. Every animation must be **interruptible**: starting a new one on the same visual must start from the current value, never snap.
4. **Idle cost:** no timers or animations running when nothing is changing. Stop `CompositionTarget.Rendering` / storyboards as soon as the target is reached.
5. Respect reduced motion: check `MotionHelper.ShouldAnimate` (`!ReduceAnimations && _uiSettings.AnimationsEnabled`), so both the in-app toggle and Windows "Animation effects" skip springs and set final values directly.
6. Do not change business logic, scoring, or telemetry code. Motion/UI only.
7. Line numbers below are from the audit; confirm by searching for the named method before editing.

---

## Phase 0: Baseline & Safety

- [ ] Commit or stash any pending workspace edits first, then create branch `motion-plan-3` (or work directly on the current branch if intentional).
- [ ] Build and run once. Note current behavior so regressions are visible.
- [ ] In `MotionHelper.cs`, keep and reuse the existing dynamic `MotionHelper.ShouldAnimate` gate (`!ReduceAnimations && _uiSettings.AnimationsEnabled`). Every helper below must early-return to final values when `ShouldAnimate` is `false`.

**Done when:** builds clean and `MotionHelper.ShouldAnimate` is confirmed as the single reduced-motion gate.

---

## Phase 1: Spring Engine in `MotionHelper.cs`

Replace the clamped `CubicBezier(0.16, 1.0, 0.3, 1.0)` / `(0.2, 0.9, 0.3, 1.0)` usage in:
`AnimateStaggeredEntrance`, `AnimateHoverLift`, `AnimatePressScale`, `AnimateSlideY`, `AnimateSlideX`.

### 1.1 Add core spring helpers

```csharp
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;

public static class MotionHelper
{
    // Keep existing ReduceAnimations, ReduceAnimationsChanged, _uiSettings, and ShouldAnimate:
    // public static bool ShouldAnimate => !ReduceAnimations && _uiSettings.AnimationsEnabled;

    // Presets: (damping, period ms)
    public static readonly (float d, int p) Soft   = (0.72f, 75);  // cards, overlays
    public static readonly (float d, int p) Snappy = (0.62f, 55);  // press / rebound
    public static readonly (float d, int p) Pill   = (0.68f, 65);  // sliding indicators

    public static void PrepareVisual(UIElement el, bool center = true)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = ElementCompositionPreview.GetElementVisual(el);
        if (center && el is FrameworkElement fe)
            v.CenterPoint = new Vector3((float)fe.ActualWidth / 2f, (float)fe.ActualHeight / 2f, 0);
    }

    public static void SpringScale(UIElement el, float target, (float d, int p) s)
    {
        var v = ElementCompositionPreview.GetElementVisual(el);
        if (!ShouldAnimate) { v.Scale = new Vector3(target, target, 1); return; }
        if (el is FrameworkElement fe)
            v.CenterPoint = new Vector3((float)fe.ActualWidth / 2f, (float)fe.ActualHeight / 2f, 0);
        var a = v.Compositor.CreateSpringVector3Animation();
        a.FinalValue = new Vector3(target, target, 1);
        a.DampingRatio = s.d;
        a.Period = TimeSpan.FromMilliseconds(s.p);
        v.StartAnimation("Scale", a);
    }

    public static void SpringTranslate(UIElement el, float x, float y, (float d, int p) s)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = ElementCompositionPreview.GetElementVisual(el);
        if (!ShouldAnimate) { v.Properties.InsertVector3("Translation", new Vector3(x, y, 0)); return; }
        var a = v.Compositor.CreateSpringVector3Animation();
        a.FinalValue = new Vector3(x, y, 0);
        a.DampingRatio = s.d;
        a.Period = TimeSpan.FromMilliseconds(s.p);
        v.StartAnimation("Translation", a);
    }

    public static void SpringOpacity(UIElement el, float target, int ms = 180)
    {
        var v = ElementCompositionPreview.GetElementVisual(el);
        if (!ShouldAnimate) { v.Opacity = target; return; }
        var a = v.Compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1f, target);
        a.Duration = TimeSpan.FromMilliseconds(ms);
        v.StartAnimation("Opacity", a);
    }
}
```

### 1.2 Rewrite existing helpers on top of the spring core

| Helper | New behavior |
|---|---|
| `AnimateHoverLift(el, bool on)` | `SpringTranslate(el, 0, on ? -3 : 0, Soft)` + `SpringScale(el, on ? 1.015f : 1f, Soft)` |
| `AnimatePressScale(el, bool pressed)` | `SpringScale(el, pressed ? 0.95f : 1f, Snappy)` (release overshoots naturally) |
| `AnimateSlideY / SlideX` | `SpringTranslate` directly from the visual's current `Translation` to the target `(x, y)` using `Pill`/`Soft` so mid-flight calls remain interruptible (only set an initial start offset on first mount/reveal, never on re-target). |
| `AnimateStaggeredEntrance` | For item `i`: delay `i * 45ms` using tracked `DispatcherQueueTimer` one-shots (stop/clear active timers on `Unloaded`), start at Opacity 0 and Translation Y +16, then `SpringTranslate(…, 0, 0, Soft)` + `SpringOpacity(…, 1)` |

### 1.3 Remove dead code

Delete the old `CubicBezier` easing construction once nothing references it.

**Done when:** build passes; hovering any existing lifted control overshoots slightly and settles; rapid in/out never snaps or stutters.

---

## Phase 2: `MainWindow` Chrome & Overlays

### 2.1 Fix `NavButton_Click` (lines ~232–234)

Bug: `AnimatePressScale(btn, true)` and `AnimatePressScale(btn, false)` are called back-to-back, which cancels the squish.

- Remove both calls from `NavButton_Click`.
- Wire the buttons' `PointerPressed` → `AnimatePressScale(btn, true)`.
- Wire `PointerReleased`, `PointerCanceled`, `PointerExited`, `PointerCaptureLost` → `AnimatePressScale(btn, false)`.
- Keep navigation logic in `Click` only.

### 2.2 Smooth sidebar `AnimateSidebar` (lines ~97–116)

Bug: `SidebarColDef.Width = new GridLength(targetWidth)` snaps 58 ↔ 204.

- Replace with a short per-frame width tween (this is the one allowed layout animation because the column is the layout): a `DispatcherQueueTimer` or `CompositionTarget.Rendering` loop interpolating width with the spring-style curve `1 - e^(-6t)·cos(10t)` over ~280ms, ending exactly on `targetWidth`.
- Unhook the render loop the moment the target is reached (zero idle cost).
- Cancel any in-flight tween when a new toggle arrives, starting from the current width.
- Fade labels via `SpringOpacity` (hide before collapsing, show after expanding past ~120px).
- `SidebarActiveIndicator`: `SpringTranslate` to the new button's Y with `Pill` preset (slight overshoot).

### 2.3 `CommandPaletteOverlay`

- Open: set `Visibility = Visible`, Opacity 0, Scale 0.95 → `SpringScale(…, 1f, Soft)` + `SpringOpacity(…, 1)`.
- Close: start `SpringScale(…, 0.97f, Soft)`, wrap the finite `SpringOpacity(…, 0, 120)` `ScalarKeyFrameAnimation` inside `Compositor.CreateScopedBatch` so `Completed` fires reliably at ~120ms, and set `Visibility = Collapsed` there.
- Never toggle `Visibility` before the exit finishes.

### 2.4 `ToastHost`

- Enter: start Translation Y +24, Opacity 0 → `SpringTranslate(…, 0, 0, Soft)` + `SpringOpacity(…, 1)`.
- Exit: start `SpringTranslate(…, 0, 12, Soft)`, wrap the finite `SpringOpacity(…, 0, 160)` `ScalarKeyFrameAnimation` in `Compositor.CreateScopedBatch`, then `ToastHost.Children.Remove(toast)` in the batch `Completed` handler (replaces the current immediate removal).
- Guard against double-remove if the toast is dismissed twice.

**Done when:** nav press squish is visible and rebounds; sidebar glides; palette and toasts animate both in and out; no `Visibility` pops remain.

---

## Phase 3: `DashboardPage` Micro-interactions

### 3.1 `ModeSlidingPill` (`UpdateModeUi`, lines ~218–224)

Bug: X animates via `AnimateSlideX` but `Width` snaps via `ModeSlidingPill.Width = targetBtn.ActualWidth`.

- Do **not** use `Visual.Scale.X` on `ModeSlidingPill` directly—it has `BorderThickness="1"` and `CornerRadius="8"`, which warp into uneven borders and oval corners under non-uniform X scaling.
- Instead:
  - Position X → `SpringTranslate(ModeSlidingPill, (float)pt.X, 0, Pill)` (interruptible from current X).
  - Width → smooth `ModeSlidingPill.Width` interpolation from its current `ActualWidth` to `targetBtn.ActualWidth` (using the same allowed render-loop tween pattern as `SidebarColDef.Width`, unhooked on settle and cancelled on re-click).
- Verify all three transitions: `BtnModeGaming` (`Ultimate Power / Gaming`) ↔ `BtnModeWork` (`Work & Dev`) ↔ `BtnModeEco` (`Silent / Battery Saver`).

### 3.2 Action & Utility Tiles (Borders in `DashboardPage.xaml`)

- Convert each tile `Border` into a `Button` (or a minimal `Button` subclass setting `ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand)` since plain WinUI 3 `Button` keeps the arrow cursor) with a shared flat `TileButtonStyle` in `Styles/Controls.xaml`: transparent background, no default hover/pressed visual states that fight the custom motion.
  - Gives: Hand cursor, Tab focus, Space/Enter activation, visible focus ring (use a 2px accent `FocusVisual`).
- Layered hover choreography (wire `PointerEntered`/`PointerExited`/`GotFocus`/`LostFocus`):
  - Tile: lift -3px + border glow (animate border brush opacity or a glow layer's Opacity via `SpringOpacity`).
  - Icon badge (30×30): `SpringScale(badge, 1.12f, Snappy)`.
  - `›` chevron: `SpringTranslate(chevron, 4, 0, Soft)`.
  - On leave/blur: everything back to rest with the same springs.
- Press: `AnimatePressScale(tile, true/false)` on pressed/released.
- Implement as one shared handler set, with named children found via the tile's template or `Tag`, so it isn't copy-pasted per tile.

### 3.3 System Monitor & Storage rows

- CPU / GPU / RAM / Disk rows and Drive C: / D: / E: rows:
  - Hover: subtle background highlight (Opacity spring on an overlay `Border`) + `SpringTranslate(x +2)`.
  - Cursor: Hand (via the same `Button` subclass / `ProtectedCursor` helper as 3.2).
  - Click: navigate to Diagnostics (monitor rows) or Storage (drive rows) using the existing navigation method; make them focusable (`IsTabStop`, or convert to Buttons using the same style as 3.2).

### 3.4 Elastic score choreography (`AnimateScoreRing`)

- Replace the current easing with the damped spring curve:
  `f(t) = 1 - exp(-6t) · cos(10t)`, `t ∈ [0,1]`, duration ~900ms, and force exactly `1.0` on the final frame.
- Drive these from the same `t` so they overshoot and settle together:
  - `HeroScoreText` (count-up value)
  - `ScoreArcCanvas` arc and its glowing tip
  - `MainScoreBar`
  - `BarHealthSubScore`, `BarPerfSubScore`, `BarSecSubScore`, `BarStabSubScore` (these currently snap. Fill them in sync, with a 40ms stagger between bars)
  - Drive rings C: / D: / E: (stagger 60ms)
- Clamp `_currentArcProgress` to `[0, 1]` (so `ScoreArcCanvas_Draw` never exceeds `359.5°` and corrupts `AddArc`), clamp `ProgressBar.Value` to `[0, 100]`, and clamp displayed numeric text to `[0, final]`. Express the elastic overshoot visually via a subtle `SpringScale` rebound on the score ring / card container instead of pushing arc or bar values past 100%.
- Single render-loop/timer for the whole choreography; unhook when `t >= 1`.

### 3.5 Sparklines (`_sparklineTimer`, 1s tick)

- On each new sample: instead of jumping, interpolate the newest point from the previous value to the new one over ~320ms (ease-out), redrawing only during that window.
- Between samples: no redraw, no render loop (idle GPU stays minimal).
- If a new sample arrives mid-interpolation, start from the current interpolated value.

**Done when:** pill stretches smoothly between all three modes; tiles are keyboard-focusable and have layered hover; bars/rings fill and overshoot in sync; sparklines glide; monitor/drive rows react and navigate.

---

## Phase 4: Verification Checklist

Run the app and confirm each item:

- [ ] Rapid hover in/out on tiles never snaps, and the spring re-targets from the current position.
- [ ] Nav press squishes and rebounds (no same-frame cancel).
- [ ] Sidebar toggles repeatedly (spam-click) without glitching or leaving a wrong width.
- [ ] Command palette and toasts animate in and out; no pops.
- [ ] Mode pill morph works for every mode pair, in both directions.
- [ ] Tab key reaches every tile with a visible focus ring; Space/Enter activates.
- [ ] Score ring, sub-score bars, and drive rings fill in sync (with container `SpringScale` rebound on settle, clamped within `0–100%` so arcs never wrap past 360° and text never exceeds its final value).
- [ ] Sparklines ease; idle (no changes) shows ~0% GPU in Task Manager.
- [ ] With Windows "Animation effects" turned off **or** the in-app `Reduce Animations` setting toggled on, everything snaps to final values with no errors.
- [ ] No exceptions in the debug output while navigating between pages mid-animation (dispose/unhook timers on `Unloaded`).

## Phase 5: Wrap-up

- [ ] Remove unused helpers/usings; keep `MotionHelper` as the single motion entry point.
- [ ] Commit per phase with message `motion: phase N, <summary>`.
- [ ] Report back: what changed per file, anything deferred, and any deviations from this plan with the reason.

---

## Spring Tuning Reference

| Feel | DampingRatio | Period (ms) |
|---|---|---|
| Soft settle (cards, overlays, toasts) | 0.72 | 75 |
| Snappy rebound (press) | 0.62 | 55 |
| Pill / indicator stretch | 0.68 | 65 |

Lower damping means more bounce. Lower period means faster. Adjust only if it feels off; keep values inside 0.62–0.72 / 55–75ms.