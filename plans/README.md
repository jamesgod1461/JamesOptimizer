# Animation Plans — James Optimizer

Audit performed 2026-10-30 by improve-animations (Emil Kowalski audit bar), commit `62b343a`. All four plans executed (build clean); re-verify feel checks on next manual run.

| # | Plan | Severity | Status |
|---|------|----------|--------|
| 001 | Gate entrance animations on the animations preference | HIGH | DONE |
| 002 | Remove sidebar nav hover margin snap | HIGH | DONE |
| 003 | Animate Toolbox card hover lift (no teleport) | MEDIUM | DONE |
| 004 | Accent glows follow accent switch (Static→Dynamic) | MEDIUM | DONE |

## Execution order

1 → 2 → 3 → 4. No dependencies between plans; each is independently verifiable. 001 touches only code-behind; 002–004 touch only XAML.

## Already correct (no action)

- Command palette opens/closes instantly — keyboard-frequency action, animation correctly absent (Raycast rule).
- Dashboard gauge re-animates from the previous score, not from zero, on revisits (parallel-session improvement).
- NavigationFrame page transitions gate on the animations preference.
