# 004 — Make effect/glow colors follow accent switches (StaticResource → DynamicResource)

- **Status**: TODO
- **Commit**: 62b343a
- **Severity**: MEDIUM
- **Category**: Cohesion & tokens
- **Estimated scope**: ~6 page XAML files, ~12 attribute swaps

## Problem

`Color="{StaticResource Color...}"` inside inline brushes/effects resolves ONCE at XAML load. When the user picks a different accent (Settings page → ThemeManager.SetAccent swaps overlay dictionaries), every DynamicResource ref restyles instantly — but these StaticResource refs keep the old hue, so glows and gradients lag behind the accent switch until app restart.

Find all occurrences (they are all inside DropShadowEffect.Color or GradientStop.Color on pages, never in Themes/):

```
grep -rn 'Color="{StaticResource Color' --include="*.xaml" OptimizerUI --exclude-dir=Themes
```

Known instances at commit 62b343a: About.xaml:134; Toolbox.xaml:41,59,60,69,101,133; SystemDiagnostics.xaml:43; NetworkBoost.xaml:41,274 (plus any others the grep returns).

## Target

For every match, replace `Color="{StaticResource ColorX}"` with `Color="{DynamicResource ColorX}"` — same key, dynamic lookup. All `ColorAccentCyan`, `ColorAccentViolet`, `ColorWarningAmber`, `ColorSuccessGreen`, `ColorDangerRed`, `ColorAccentGlow` keys exist in both Themes/ThemeColors.xaml and Themes/Colors.Light.xaml and are re-overridden by accent overlays, so DynamicResource always resolves.

## Repo conventions to follow

Pages already use `{DynamicResource ColorAccentGlow}` etc. in newer code (MainWindow command palette modal, Dashboard effects). This plan makes the remaining stragglers match.

## Steps

1. Run the grep above.
2. For each match, apply the one-token swap (StaticResource → DynamicResource). Do not change anything else on the line.

## Boundaries

- Do NOT touch files under OptimizerUI/Themes/ (their StaticResource cross-references inside a single dictionary are intentional and load-order safe).
- Do NOT rename keys or alter BlurRadius/Opacity values.

## Verification

- **Mechanical**: `dotnet build OptimizerUI/OptimizerUI.csproj -v q -nologo` → 0 errors. Re-run the grep → zero matches outside Themes/.
- **Feel check**: run app → Settings → click each accent swatch → glow/gradient accents on Toolbox, Network Boost, Diagnostics, About cards must retint immediately, with no restart.
- **Done when**: grep is clean and accent switching retints every page live.
