# 003 — Animate the Toolbox card hover lift instead of snapping

- **Status**: TODO
- **Commit**: 62b343a
- **Severity**: MEDIUM
- **Category**: Physicality & interruptibility
- **Estimated scope**: 1 file, 1 style block

## Problem

`ToolCardStyle` (OptimizerUI/Toolbox.xaml:30-42) swaps the card's `RenderTransform` via a hover trigger Setter:

```xml
<Setter Property="RenderTransform">
    <Setter.Value>
        <TranslateTransform Y="-2"/>
    </Setter.Value>
</Setter>
```

A Setter swap is not a transition — the card teleports 2px up the instant the mouse enters and teleports back on leave. It reads as a glitch, not a lift. (The card base style declares `RenderTransform` = `TranslateTransform Y=0`, Toolbox.xaml ~line 23.)

## Target

Replace the `RenderTransform` Setter inside the `IsMouseOver` trigger with animated Enter/Exit actions on the existing transform:

```xml
<Trigger Property="IsMouseOver" Value="True">
    <Setter Property="Background" Value="{DynamicResource SurfaceHover}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderAccent}"/>
    <Setter Property="Effect">
        <Setter.Value>
            <DropShadowEffect Color="{DynamicResource ColorAccentGlow}" Opacity="0.25" BlurRadius="28" ShadowDepth="0"/>
        </Setter.Value>
    </Setter>
    <Trigger.EnterActions>
        <BeginStoryboard>
            <Storyboard>
                <DoubleAnimation Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.Y)"
                                 To="-2" Duration="0:0:0.14">
                    <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
            </Storyboard>
        </BeginStoryboard>
    </Trigger.EnterActions>
    <Trigger.ExitActions>
        <BeginStoryboard>
            <Storyboard>
                <DoubleAnimation Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.Y)"
                                 To="0" Duration="0:0:0.14">
                    <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction>
                </DoubleAnimation>
            </Storyboard>
        </BeginStoryboard>
    </Trigger.ExitActions>
</Trigger>
```

Values per audit catalog: hover motion 125–200ms, ease-out, transform-only (GPU-friendly), 2px lift kept subtle.

## Repo conventions to follow

`Themes/ControlStyles.xaml` PrimaryActionButton (style triggers section) uses exactly this Enter/Exit `BeginStoryboard` + `CubicEase EaseOut` pattern for hover motion — copy that structure.

## Steps

1. OptimizerUI/Toolbox.xaml: in `ToolCardStyle`'s hover trigger, delete the `RenderTransform` Setter, add the Enter/Exit actions above.
2. While editing the same trigger, change the effect color from `{StaticResource ColorAccentCyan}` to `{DynamicResource ColorAccentGlow}` (see plan 004 rationale) — same line block, keeps accent switching consistent.

## Boundaries

- Do NOT touch the base style's `RenderTransform` declaration (must remain `TranslateTransform Y=0`).
- Do NOT touch other Toolbox styles (GreenGhostButtonStyle, VioletPrimaryButtonStyle, etc.) except where plan 004 applies.
- If the style has drifted since 62b343a, adapt to the actual current trigger but keep values identical.

## Verification

- **Mechanical**: `dotnet build OptimizerUI/OptimizerUI.csproj -v q -nologo` → 0 errors.
- **Feel check**: run app → Toolbox → sweep the mouse across all tool cards: each card should glide 2px up over ~140ms and glide back — no teleport. Enter/leave rapidly mid-animation: motion should retarget smoothly (BeginStoryboard on a DoubleAnimation retargets; confirm no stuck offset).
- **Done when**: no trigger Setter swaps `RenderTransform` in ToolCardStyle.
