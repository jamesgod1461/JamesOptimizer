# 001 — Gate page entrance animations on the animations preference

- **Status**: TODO
- **Commit**: 62b343a
- **Severity**: HIGH
- **Category**: Accessibility
- **Estimated scope**: 5 files, 1 line each

## Problem

The app has a user-facing "Animations" toggle persisted via `ThemeManager.Settings.AnimationsEnabled`.
`NavigationFrame.NavigateWithAnimationAsync` (OptimizerUI/NavigationFrame.cs, `if (!ThemeManager.AnimationsEnabled)`) honors it for page transitions — but every page's staggered card entrance ignores it. With animations off, cards still fly in with 200–250ms translate+fade on every page visit. A broken preference is worse than no preference.

Cited locations (first line of each method):

- OptimizerUI/SystemCleanups.xaml.cs:42 — `private void PlayStaggeredEntrance()`
- OptimizerUI/PerformanceTweaks.xaml.cs:44 — `private void PlayStaggeredEntrance()`
- OptimizerUI/Toolbox.xaml.cs:45 — `private void PlayStaggeredEntrance()`
- OptimizerUI/NetworkBoost.xaml.cs:77 — `private void PlayStaggeredEntrance()`
- OptimizerUI/About.xaml.cs:48 — `private void PlayStaggeredEntrance()`

## Target

First statement of each `PlayStaggeredEntrance()` body:

```csharp
if (!ThemeManager.AnimationsEnabled) return;
```

Cards then appear instantly (their XAML declares `Opacity="0"` and the method sets them visible via `BeginAnimation`; see "Steps" — the early return must still make elements visible).

## Repo conventions to follow

`OptimizerUI/NavigationFrame.cs` already gates this exact way:

```csharp
if (!ThemeManager.AnimationsEnabled)
{
    base.Navigate(source);
    return true;
}
```

## Steps

1. In each of the 5 files, insert as the first line of `PlayStaggeredEntrance()`:
   `if (!ThemeManager.AnimationsEnabled) return;`
2. IMPORTANT: in `About.xaml.cs` (and any page whose XAML sets `Opacity="0"` on the cards), an early return would leave the page invisible. For `About.xaml.cs` and any page where `PlayStaggeredEntrance` is the only thing setting opacity to 1, replace the early return with a loop that sets `Opacity = 1` and resets transforms instead of animating:
   ```csharp
   if (!ThemeManager.AnimationsEnabled)
   {
       // Show everything immediately, no motion
       foreach (var card in new FrameworkElement[] { CardPcSpecs, CardNetworkInfo, CardDevInfo, CardVersion })
       {
           card.Opacity = 1;
           if (card.RenderTransform is TranslateTransform tt) tt.X = 0;
       }
       return;
   }
   ```
   For the other 4 pages, inspect whether the page XAML declares `Opacity="0"` on animated elements (SystemCleanups and PerformanceTweaks do: `Opacity="0"` on Card1/Card2/… and buttons). For those pages, do the same: when animations are off, set `Opacity = 1` and zero out each `TranslateTransform` (e.g. `TransCard1.Y = 0`) for every animated element, then return. NetworkBoost and Toolbox: check their OnNavigatedTo/XAML; if they set `element.Opacity = 0` inside `AnimateCardIn`, the early return alone is safe (elements keep default opacity 1) — verify per file and apply the minimal correct variant.
3. Do not modify `NavigationFrame.cs` — it is already correct.

## Boundaries

- Do NOT touch Themes/*.xaml, NavigationFrame.cs, or ThemeManager.cs.
- Do NOT change animation timings for the enabled case.
- Do NOT change markup/structure — code-behind only.
- If a file has drifted (method renamed/removed since commit 62b343a), STOP and report instead of improvising.

## Verification

- **Mechanical**: `dotnet build OptimizerUI/OptimizerUI.csproj -v q -nologo` → 0 errors, 0 warnings.
- **Feel check**: run app → Settings → toggle Animations OFF → visit System Cleanups, Performance Tweaks, Toolbox, Network Boost, About → all content must be fully visible instantly with zero motion; toggle back ON → staggered entrances return.
- **Done when**: with the toggle off no element on any page is left invisible (Opacity stuck at 0) and no storyboard runs.
