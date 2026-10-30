# 002 — Remove the sidebar nav hover margin snap

- **Status**: TODO
- **Commit**: 62b343a
- **Severity**: HIGH
- **Category**: Purpose & frequency (+ Performance)
- **Estimated scope**: 1 file, 1 line

## Problem

Every hover over every sidebar nav item (dozens of times per day) changes the row's `Margin` from `8,2,8,2` to `10,2,6,2` via a trigger Setter. Margin is a layout property: the row *snaps* sideways ~2px with zero transition, causing a visible twitch and a full layout pass of the sidebar on hover. Per the audit catalog: hover effects hit tens-to-hundreds of times per day → remove or drastically reduce; never animate layout properties.

Cited location — OptimizerUI/MainWindow.xaml:137 (inside the `IsMouseOver`+`!IsChecked` MultiTrigger of `SidebarNavItemStyle`):

```xml
<Setter TargetName="RowBorder" Property="Margin" Value="10,2,6,2"/>
```

## Target

Delete that one `<Setter>` line. The hover state keeps its background, border, icon fill and label color feedback (all brush-only, cheap). Base `Margin="8,2,8,2"` on the RowBorder (MainWindow.xaml ~line 142) stays.

## Repo conventions to follow

The active nav trigger in the same style (`IsChecked=True`) does NOT change margins — it communicates selection with an accent bar + gradient. Hover communicating with brushes only matches that exemplar.

## Steps

1. OptimizerUI/MainWindow.xaml: remove line 137 (`<Setter TargetName="RowBorder" Property="Margin" Value="10,2,6,2"/>`).

## Boundaries

- Do NOT touch any other trigger, the IsChecked trigger, or the label styles.
- Do NOT modify MainWindow.xaml.cs.
- If the line has drifted (margin value changed since 62b343a), remove whichever Setter targets `RowBorder` `Margin` inside the hover MultiTrigger; if none exists, report "already fixed".

## Verification

- **Mechanical**: `dotnet build OptimizerUI/OptimizerUI.csproj -v q -nologo` → 0 errors.
- **Feel check**: hover up and down the sidebar list quickly — rows must no longer twitch sideways; hover feedback (background + border) still clearly visible.
- **Done when**: no trigger in `SidebarNavItemStyle` sets `Margin`.
