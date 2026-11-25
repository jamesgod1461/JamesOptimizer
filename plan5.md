# Plan 5: Raised Tactile Depth System, Typography/Spacing Fixes & Elastic Motion

**Project:** JamesOptimizer V3 (`JamesOptimizer.WinUI`, C# / .NET 10, WinUI 3, Win2D, `Microsoft.UI.Composition`)  
**Scope:** Strictly UI, typography/spacing, styling, and Composition motion. Do **not** modify `JamesOptimizer.Core`, `JamesOptimizer.Service`, IPC (`IpcProtocol`), telemetry, or scoring logic.

---

## Core Design Rules

1. **Tactile Hierarchy (`Raised` / `Inset` / `Flat`):**
   - **Raised (`L2` / `L3` + dual shadow):** Clickable actions (Quick Action tiles, System Utility tiles, primary/secondary/icon/ghost-hover buttons, unselected segmented pills, sidebar hover plates).
   - **Inset (`Bg.Inset` + top inner shadow):** Editable fields & dropdowns (`ComboBox`, `TextBox`, `ComboAutoFreq`, `TitleBarSearchBtn`) and **selected-in** active states (active segmented pill, active sidebar item).
   - **Flat (`L1`):** Informational surfaces (cards, labels, read-only stats, sparklines, status badges). Never add drop shadows or lift motion to cards.
2. **Composition-Only Motion:** Animate only `Scale`, `Translation`, `Opacity`, and `DropShadow` (`Offset`, `BlurRadius`, `Opacity`). Never animate `Width`, `Height`, `Margin`, or `Padding` on pointer hover/press.
3. **Zero Handler Collisions:** Remove legacy per-page hover/press handlers (`Tile_PointerEntered`, `Row_PointerEntered`, `MotionHelper.AttachPressSpring`) wherever `ElasticBehavior` is attached via style so only **one** controller drives `Visual.Scale` and `Visual.Translation`.
4. **Reduced Motion & Idle Safety:** Gate every spring, shadow transition, icon nudge, score sweep, and CTA pulse on `MotionHelper.ShouldAnimate` (`ThemeEffects.EffectsEnabled && _uiSettings.AnimationsEnabled`). Pause all active loops on `Page_Unloaded` and when `ThemeEffects.IsWindowVisibleAndActive == false`.
5. **Phase Build Gate:** Run `dotnet build JamesOptimizer.WinUI\JamesOptimizer.WinUI.csproj -c Debug -p:Platform=x64` after every phase and fix all warnings/errors before moving on.

---

## Phase 1: Design Tokens & Top-Lit Depth System (`Theme/Tokens.xaml`)

Extend `Theme/Tokens.xaml` inside both `Default` (Dark) and `Light` `ThemeDictionaries` so runtime theme switching (`SettingsPage`) preserves full contrast.

### 1.1 Elevation & Surface Palette

| Token | Role | Dark (`Default`) | Light |
|---|---|---|---|
| `Bg.App` | `L0` app canvas | `#090B0E` | `#F3F4F6` |
| `Bg.Sidebar` | Navigation rail & title bar | `#0C0E13` | `#EAECEF` |
| `Bg.Surface` | `L1` flat cards & panels | `#0F1217` | `#FFFFFF` |
| `Bg.Surface2` | Elevated neutral badge/chip base | `#171B22` | `#F0F2F5` |
| `Bg.Inset` | Recessed inputs & dropdowns | `#0B0D11` | `#E6E9EE` |
| `Brush.Raised.Idle` | `L2` vertical gradient (0,0 → 0,1) | `#2B3038` → `#1B1F26` | `#FFFFFF` → `#EFF2F6` |
| `Brush.Raised.Hover` | `L3` vertical gradient (0,0 → 0,1) | `#323843` → `#20252D` | `#FFFFFF` → `#E5E9F0` |
| `Brush.Raised.Pressed` | Pressed / selected-in gradient | `#161A20` → `#1E2229` | `#DDE2E9` → `#EAEEF3` |
| `Brush.Cta.Idle` | Primary CTA vertical gradient | `#163640` → `#0E2229` | `#0E7490` → `#155E75` |
| `Brush.Cta.Hover` | Primary CTA hover gradient | `#1C4450` → `#122B34` | `#0891B2` → `#0E7490` |
| `Brush.Cta.Pressed` | Primary CTA pressed gradient | `#0D2027` → `#142F38` | `#164E63` → `#0E7490` |

### 1.2 Directional Borders, Inner Hairlines, Accents & Motion Constants

- **Directional raised borders (`LinearGradientBrush`, 0,0 → 0,1):**
  - `Border.Raised.Idle`: `#1AFFFFFF` (`rgba(255,255,255,0.10)`) top → `#A6000000` (`rgba(0,0,0,0.65)`) bottom.
  - `Border.Raised.Hover`: `#29FFFFFF` (`0.16`) top → `#A6000000` bottom.
  - `Border.Inset`: `#80000000` top → `#14FFFFFF` bottom (recessed field edge).
- **1px inner hairlines (`BorderThickness="0,1,0,0"`):**
  - `Border.InnerHighlight`: `#0FFFFFFF` (`rgba(255,255,255,0.06)`).
  - `Border.InnerPressedShadow`: `#73000000` (`rgba(0,0,0,0.45)`).
- **Accent & Rim Glow Tokens (`25–35%` opacity):**
  - Accents: `Accent.Cyan` (`#22D3EE`), `Accent.Purple` (`#A78BFA`), `Accent.Green` (`#34D399`), `Accent.Yellow` (`#FBBF24` amber).
  - Rims: `Rim.Cyan` (`#4D22D3EE`), `Rim.Purple` (`#4DA78BFA`), `Rim.Green` (`#4D34D399`), `Rim.Yellow` (`#4DFBBF24`).
- **Shadow & Spring Constants (`Tokens.xaml`):**
  - Contact shadow: Idle `Offset (0, 2, 0)`, `BlurRadius 4`, `Opacity 0.60`; Hover `Offset (0, 4, 0)`, `BlurRadius 8`; Pressed `Offset (0, 1, 0)`, `BlurRadius 2`.
  - Ambient shadow: Idle `Offset (0, 8, 0)`, `BlurRadius 18`, `Opacity 0.45`; Hover `Offset (0, 14, 0)`, `BlurRadius 28`; Pressed `Offset (0, 2, 0)`, `BlurRadius 4`.
  - Spring constants: `DampingRatio = 0.50`, `PeriodMs = 50`, `HoverScale = 1.04`, `HoverLiftY = -2`, `PressScaleX = 0.96`, `PressScaleY = 0.97`, `PressContentOffsetY = 1`.
- **WinUI 3 ThemeSwitch Safety Note:** Avoid `{ThemeResource ...}` directly inside `VisualState.Setters` for `LinearGradientBrush` if WinUI throws/freezes on live theme toggle; keep raw `Color` keys in `ThemeDictionaries` and test Dark → Light → Dark switching in `SettingsPage`.

**Done when:** `Tokens.xaml` defines all elevation, gradient, border, rim, shadow, and motion tokens for Dark and Light themes and builds clean.

---

## Phase 2: Reusable Raised, Primary, Icon, Ghost, Segmented & Inset Styles (`Styles/Controls.xaml`)

Refactor `Styles/Controls.xaml` so every interactive control gets top-lit physical depth, focus visuals, and disabled states without per-page XAML duplication.

### 2.1 Shared Raised ControlTemplate Anatomy

Every raised `Button` template (`RaisedButtonStyle`, `PrimaryRaisedButtonStyle`, `IconRaisedButtonStyle`, `TileButtonStyle`, `RowButtonStyle`) uses this exact visual order:

1. `PART_ShadowHost` (`Grid`, `IsHitTestVisible="False"` — **first child** so Composition `DropShadow` `SpriteVisual`s sit behind the button surface).
2. `PART_BaseBorder` (`Border` with `Background`, `BorderBrush`, `BorderThickness="1"`, `CornerRadius`).
3. `PART_InnerHighlight` (`Border` with `BorderThickness="0,1,0,0"`, `Margin="1,1,1,0"`, `CornerRadius`, `IsHitTestVisible="False"`).
4. `PART_AccentRim` (`Border` with `BorderThickness="1"`, `BorderBrush="{ThemeResource Rim.Cyan}"`, `Opacity="0"`, `CornerRadius`, `IsHitTestVisible="False"`).
5. `PART_ContentPresenter` (`ContentPresenter` with `RenderTransform` `<TranslateTransform x:Name="PART_ContentShift" />` for the `+1px` pressed content shift).

### 2.2 Required Control Styles

| Style Key | Target Usage | States (`Normal` / `PointerOver` / `Pressed` / `Focused` / `Disabled`) |
|---|---|---|
| `RaisedButtonStyle` *(and implicit `Button` / `SecondaryButtonStyle` / `ModernGhostButtonStyle` / `TileButtonStyle` / `RowButtonStyle`)* | Quick Action tiles, System Utility tiles, `REPORT`, `RUN NOW`, `MANAGE STORAGE`, action buttons | **Normal:** `Brush.Raised.Idle`, `Border.Raised.Idle`, `Border.InnerHighlight`. **PointerOver:** `Brush.Raised.Hover`, `Border.Raised.Hover`, `PART_AccentRim.Opacity=1`. **Pressed:** `Brush.Raised.Pressed`, `Border.InnerPressedShadow`, `PART_ContentShift.Y=1`. **Focused:** 2px `Accent.Cyan` focus visual. **Disabled:** `Opacity=0.40`, `PART_ShadowHost.Opacity=0`. |
| `PrimaryRaisedButtonStyle` & `HeroMissionButtonStyle` | `START SYSTEM OPTIMIZATION`, primary page CTAs (`ModernPrimaryButtonStyle`) | Same template with `Brush.Cta.Idle` → `Brush.Cta.Hover` → `Brush.Cta.Pressed`, `PART_AccentRim` rest `0.45` → hover `1.0`, `ElasticBehavior.HoverScale="1.025"` on full-width hero CTA and `ShadowIntensity="1.2"`. |
| `IconRaisedButtonStyle` | `BtnRescan`, `BtnRefresh`, drawer close, delete icon buttons | Compact `34×34` (`Padding="0"`, `CornerRadius="10"` or pill `17`) using `RaisedButtonStyle` template so icon buttons pop off L1 cards. |
| `GhostButtonStyle` *(updated)* | `"View Details →"` and subtle inline row actions | **Normal:** `Transparent` fill/border, `PART_ShadowHost.Opacity=0`. **PointerOver:** reveals `Brush.Raised.Hover`, `Border.Raised.Hover`, and soft contact shadow. |
| `InsetFieldStyle` *(and implicit `TextBox`, `ComboBox`, `ModernSearchBoxStyle`)* | `ComboAutoFreq` (Schedule), search boxes, HOSTS/alias inputs, settings dropdowns | `Bg.Inset` fill, `Border.Inset` border, inner top dark hairline so inputs read recessed beneath L1 cards. |
| `SegmentedPillButtonStyle` & `SegmentedPillActiveButtonStyle` | Power mode switcher (`Ultimate Gaming` / `Work & Dev` / `Silent Eco`), page tabs | **Unselected:** `L2` raised pill (`Brush.Raised.Idle`, `Border.Raised.Idle`). **Selected:** pressed-in plate (`Brush.Raised.Pressed`, `Border.InnerPressedShadow`, cyan rim). |
| `SidebarNavButtonStyle` | `MainWindow` navigation items | **Normal:** transparent. **PointerOver:** soft raised `L2` plate (`Brush.Raised.Idle`). **Selected:** pressed-in `Brush.Raised.Pressed` plate + `SidebarActiveIndicator` cyan bar. |

**Done when:** `Controls.xaml` compiles with all styles wired to `core:ElasticBehavior.IsEnabled="True"` and zero legacy WinUI gray/blue hover templates remaining.

---

## Phase 3: `ElasticBehavior` & Composition Spring Engine (`Core/MotionHelper.cs`, `Core/ElasticBehavior.cs`, `Core/TileButton.cs`)

### 3.1 Upgrade `MotionHelper.cs`

Replace `CreateCubicBezierEasingFunction` in `SpringScale` and `SpringTranslate` with true Composition springs:
- Use `Compositor.CreateSpringVector3Animation()` (`SpringVector3NaturalMotionAnimation`):
  - **Hover spring:** `DampingRatio = 0.50f`, `Period = TimeSpan.FromMilliseconds(50)` (`1.00 → ~1.06 → 1.04` on enter; `1.04 → ~0.99 → 1.00` on leave; `< 350ms` visual settle).
  - **Press squash-and-stretch (`SpringScaleXY`):** non-uniform `Vector3(0.96f, 0.97f, 1f)` on pointer down; on pointer up, release springs back past `1.0` (`~1.03`) and settles at `HoverScale` (`1.04`) if pointer remains over or `1.00` if outside.
  - **Always call `ElementCompositionPreview.SetIsTranslationEnabled(el, true)`** before animating `"Translation"`, and update `Visual.CenterPoint` on `SizeChanged` so non-uniform squash scales around the true center.
  - **Interruptibility:** Do **not** reset `InitialVelocity` or snap current values when re-targeting an active spring.
  - **Reduced motion:** When `!MotionHelper.ShouldAnimate`, assign `Visual.Scale`, `Visual.Properties["Translation"]`, and shadow values immediately with zero animation.

### 3.2 Create `Core/ElasticBehavior.cs`

Implement a reusable attached-property behavior applied through the Phase 2 styles:

- **Attached Properties:**
  - `IsEnabled` (`bool`): subscribes/unsubscribes `Loaded`, `Unloaded`, `SizeChanged`, `IsEnabledChanged`, `PointerEntered`, `PointerExited`, `PointerPressed` (`handledEventsToo: true`), `PointerReleased` (`handledEventsToo: true`), `PointerCanceled`, `PointerCaptureLost`.
  - `HoverScale` (`double`, default `1.04`)
  - `HoverLiftY` (`double`, default `-2.0`)
  - `ShadowIntensity` (`double`, default `1.0`; `0.75` for System Utilities, `1.2` for Hero CTA)
  - `ShadowCornerRadius` (`double`, default `10.0`)
  - `NudgeChildIcons` (`bool`, default `true`)
- **Dual `DropShadow` `SpriteVisual`s on `PART_ShadowHost`:**
  - Create a `ContainerVisual` containing two `SpriteVisual`s (`contactShadow` + `ambientShadow`) attached via `ElementCompositionPreview.SetElementChildVisual(shadowHost, container)`.
  - Match `SpriteVisual.Size` on `SizeChanged` (inset by `1–2px` to prevent rectangular shadow corner bleed behind rounded borders, or apply `CompositionGeometricClip` / rounded mask).
  - Animate `DropShadow.BlurRadius`, `DropShadow.Offset`, and `SpriteVisual.Opacity` via finite `160–220ms` ease-out Composition animations synchronized with hover lift and press squash.
  - Hide shadows (`Opacity = 0`) when `Control.IsEnabled == false`, and clean up visual references on `Unloaded`.
- **Automatic Child Icon & Chevron Nudge:**
  - Discover `PART_IconChip` (or 30×30 icon badge) and `PART_Chevron` (`›`) inside the button content: on hover, nudge icon `+2px` (`SpringTranslate`) and slide chevron `+3px` right (`SpringTranslate`).

### 3.3 Clean Up `TileButton.cs`

Remove the duplicate `MotionHelper.AttachPressSpring(this)` call from `TileButton()` (or gate it when `ElasticBehavior.GetIsEnabled(this)` is true) so `TileButton` only provides `Hand` cursor and template part helpers without competing with `ElasticBehavior`.

**Done when:** Any button styled with `RaisedButtonStyle` / `PrimaryRaisedButtonStyle` / `IconRaisedButtonStyle` exhibits real animated dual shadows, rubber hover overshoot, squash-and-stretch press, and clean teardown on `Unloaded`.

---

## Phase 4: Shell & Dashboard Rollout + Text/Spacing Fixes (`MainWindow.xaml(.cs)`, `DashboardPage.xaml(.cs)`)

### 4.1 Dashboard & Shell Text/Spacing Audit Fixes (from UI Review)

Fix the existing text and spacing defects before/alongside applying the new raised styles:
1. **Fix `"Welcome back,JA"` zero-space bug (`DashboardPage.xaml`):** Replace the two adjacent `TextBlock`s with a single `TextBlock` containing `<Run Text="Welcome back," /><Run Text=" JAMES" FontWeight="Bold" />` so DirectWrite does not strip the trailing space.
2. **Fix `CardTopCommandDeck` crowding:** Add `ColumnSpacing="16"`, `TextTrimming="CharacterEllipsis"` on the subtitle, and crisp mode labels (`Ultimate Gaming`, `Work & Dev`, `Silent Eco`) so nothing clips when the sidebar expands.
3. **Fix `ScoreRingContainer` & Hero header:** Set `HeroScoreText` `LineHeight="40"` (`LineStackingStrategy="BlockLineHeight"`) for optical centering inside the `124×124` ring; add explicit `ColumnDefinitions` (`*`, `Auto`) on the headline row (preventing overlap with `BtnRescan`) and on every 2×2 sub-score label row.
4. **Fix System Monitor column clipping (`DashboardPage.xaml`):** Widen `Column 0` from `74` to `112` and standardize row heights (`46px`) so `RamSubLabelText` (`"18.3 / 31.8 GB"`) and `GpuSubLabelText` never truncate prematurely.
5. **Preserve/verify Sidebar alignment (`MainWindow.xaml`):** Keep `ToggleSidebarBtn` left-aligned (`HorizontalContentAlignment="Left"`, `Padding="0,0,12,0"`, `Margin="8,10,8,6"`) matching `NavItemsPanel` (`Margin="8,2"`) and `SidebarActiveIndicator` (`Margin="2,12,0,0"`, `Height="18"`).

### 4.2 Control Mapping on `MainWindow` & `DashboardPage`

- **Quick Action tiles (6×):** Apply `RaisedButtonStyle` + `ElasticBehavior`; style each `30×30` icon chip (`PART_IconChip`) as a mini raised container (`Brush.Raised.Idle`, top highlight, subtle shadow) whose icon brightens to accent color on hover; remove `Tile_PointerEntered`/`Tile_PointerExited` from XAML and `.xaml.cs`.
- **System Utility tiles (4×):** Apply `RaisedButtonStyle` with `core:ElasticBehavior.ShadowIntensity="0.75"`.
- **Power Mode Switcher:** Style tray with `Bg.Inset` + `Border.Inset`; unselected mode buttons get subtle `L2` raised look; `ModeSlidingPill` styled as pressed-in plate (`Brush.Raised.Pressed`, `Border.InnerPressedShadow`, `Accent.Cyan` rim) animated via `SpringTranslate` (`ModeSlidingPill` synced on `SizeChanged`).
- **`REPORT`, `RUN NOW`, `MANAGE STORAGE →`:** Apply `SecondaryButtonStyle` (now backed by `RaisedButtonStyle`).
- **`BtnRescan`:** Apply `IconRaisedButtonStyle` (`32×32` raised square).
- **`View Details →`:** Apply `GhostButtonStyle` (transparent idle → raised pill on hover).
- **`ComboAutoFreq` (Schedule dropdown):** Apply `InsetFieldStyle` (`Bg.Inset`, recessed edge).
- **`HeroStartMissionBtn` (`START SYSTEM OPTIMIZATION`):** Apply `HeroMissionButtonStyle` (`PrimaryRaisedButtonStyle`, `HoverScale="1.02"`, `ShadowIntensity="1.2"`) plus a Composition 3s sinusoidal/keyframe idle glow pulse (`±8%` opacity on `PART_AccentRim`) that pauses when `!MotionHelper.ShouldAnimate` or `Page_Unloaded`.
- **Extra Dashboard Polish:**
  - Card staggered entrance: `35ms` stagger, `220ms` duration (`MotionHelper.AnimateStaggeredEntrance`).
  - Score ring sweep on load: `700ms` ease-out in `AnimateScoreRing` (`DashboardPage.xaml.cs`).
- **Sidebar & TitleBar (`MainWindow.xaml(.cs)`):** Apply `SidebarNavButtonStyle` (hover raised plate, active pressed-in plate + cyan left indicator), remove redundant `MotionHelper.AttachPressSpring(navBtn)`, ensure `TitleBarSearchBtn` reads clearly as an inset `Ctrl+K` field, and keep caption button hover fills crisp.

**Done when:** `MainWindow` and `DashboardPage` build clean, all hover/press/focus/disabled states work uniformly, and 1 Hz `SystemMetricsService` updates maintain smooth 60 fps motion.

---

## Phase 5: Full Rollout & Text/Spacing Cleanup Across All 10 Views (`Views/*.xaml`)

Roll out the shared styles and fix per-page layout/spacing issues across every remaining view:

1. **Global header & grid fixes across all 10 views:**
   - Add `ColumnSpacing="16"` to every page header `<Grid>` (`*`, `Auto`) and `TextWrapping="Wrap"` on header `Type.Caption` subtitles.
   - Replace every single-column `<Grid>` that overlays left text + right `HorizontalAlignment="Right"` controls with explicit `ColumnDefinitions` (`*`, `Auto`) + `ColumnSpacing="16"` (specifically in `CleanupsPage`, `PerformancePage`, `GamingModePage`, `NetworkPage`, `ToolboxPage`, `DiagnosticsPage`, and `SettingsPage`).
   - Set `MinWidth="44"` on label-less `ToggleSwitch`es (`OnContent="" OffContent=""`) in `PerformancePage` and `NetworkPage` so WinUI doesn't reserve 154px of blank space.
   - Remove hardcoded `MaxWidth="520"` / `MaxWidth="540"` / `MaxWidth="560"` inside `*` Grid columns (`StartupPage`, `ServicesPage`, `StoragePage`, `ToolboxPage`) so `TextTrimming="CharacterEllipsis"` adapts naturally to window width.
2. **Per-page style rollout:**
   - **`CleanupsPage.xaml`:** `TabJunk`/`TabAppx`/`TabDeep` segmented pills; `Scan` / `Clean Selected` / per-row `Clean` / Deep clean buttons raised; `TxtAppxSearch` inset; fix `ScanProgressBar` asymmetric `Margin="0,8,40,0"`.
   - **`PerformancePage.xaml`:** `RESTORE POINT`, `DETECT`, `REVERT ALL` raised secondary; 5 Preset profile cards upgraded to `RaisedButtonStyle` + `ElasticBehavior`; `SearchBox` inset; category pills segmented; per-tweak `Preview` raised.
   - **`GamingModePage.xaml`:** `ACTIVATE GAME TURBO` (`PrimaryRaisedButtonStyle`); replace 2×3 manual spacer rows/columns with `ColumnSpacing="16" RowSpacing="16"` and `RowDefinitions="*, Auto"` per card so all 6 raised action buttons align across bottoms; Daemon inputs/combo inset; `Add Rule` & `Remove` raised.
   - **`StartupPage.xaml`:** `REFRESH` raised; `TxtStartupSearch` inset; scope tabs segmented; delete icon button `IconRaisedButtonStyle`.
   - **`ServicesPage.xaml`:** `DISABLE TELEMETRY SERVICES` (`PrimaryRaisedButtonStyle`), `BtnRefresh` (`IconRaisedButtonStyle`), row `Start/Stop` raised.
   - **`NetworkPage.xaml`:** Segmented tabs; `ComboAdapter` & HOSTS inputs inset; wrap the 7 DNS buttons into a 4-column grid (`ColumnSpacing="8" RowSpacing="8"`) so nothing clips horizontally; DNS / Flush / Reset / `Remove` / `Location` raised; `Run Live UDP DNS Benchmark`, `Add Entry`, `Refresh Connections` (`PrimaryRaisedButtonStyle`).
   - **`StoragePage.xaml`:** Mode tabs + `cleanmgr.exe` raised; `ComboScanRoot`, `ComboDownloadAge` (`Width="175"`), `TxtDupExclusions` inset; primary scan buttons `PrimaryRaisedButtonStyle`; `Recycle` / `Auto-Select` raised.
   - **`ToolboxPage.xaml`:** 4 mode tabs; `RUN BENCHMARK` (`PrimaryRaisedButtonStyle`), `Export CSV` raised; fix 3-card Quick Utilities bottom alignment (`RowDefinitions="*, Auto"`); fix `AliasesList` overlapping single-column Grid (`ColumnDefinitions="140, *, Auto"`); File Unlocker / Integrator / winget inputs inset & buttons raised.
   - **`DiagnosticsPage.xaml`:** Replace 2×2 manual spacers with `ColumnSpacing="16" RowSpacing="16"`; add `ColumnDefinitions` (`*`, `Auto`) to all 5 checklist rows; `DIAGNOSTIC BUNDLE` / `AUDIT & REVERT LOG` raised; `EXPORT REPORT` (`PrimaryRaisedButtonStyle`).
   - **`SettingsPage.xaml`:** Add `ColumnDefinitions` (`*`, `Auto`) + `ColumnSpacing="16"` to all 8 settings rows; all dropdowns & `TxtProfilePath` inset; `Export Profile` (`PrimaryRaisedButtonStyle`); `Ping & Handshake`, `Apply to Service`, `Dry-Run Import`, `Apply Import`, `PANIC REVERT ALL`, `Test Latency`, and About links raised.

**Done when:** Every page under `Views/` builds clean with zero hardcoded flat button styles and zero overlapping/clipping text.

---

## Phase 6: Polish, DPI, Accessibility & Performance QA

1. **Build & Unit Test Verification:**
   - `dotnet build JamesOptimizer.WinUI\JamesOptimizer.WinUI.csproj -c Debug -p:Platform=x64` (0 warnings, 0 errors).
   - `dotnet test JamesOptimizer.Tests\JamesOptimizer.Tests.csproj` (confirm zero regressions).
2. **DPI Scaling Verification (`100%`, `125%`, `150%`):**
   - Inspect `MainWindow`, `DashboardPage`, `PerformancePage`, `NetworkPage`, and `SettingsPage` at all three DPI scales. Verify 1px top highlights, directional borders, `SpriteVisual` shadow bounds on resize, and `ModeSlidingPill` alignment remain crisp.
3. **Keyboard Focus & Disabled States:**
   - Tab through interactive controls on each page to verify the 2px `Accent.Cyan` focus rim renders cleanly, and confirm disabled buttons render at `0.40` opacity with shadows suppressed.
4. **Reduced Motion & Live Telemetry Perf:**
   - Verify Windows **Settings → Accessibility → Visual effects → Animation effects = Off** and in-app **Settings → Reduce Animations** both disable springs, shadow transitions, score sweep, and CTA pulse immediately.
   - Leave `DashboardPage` open during 1 Hz `SystemMetricsService` updates and navigate across pages to confirm smooth 60 fps interaction, zero animation leaks on hidden pages, and zero active loops when minimized.

---

## Acceptance Checklist

- [ ] Every clickable element is visually distinguishable from the background at a glance
- [ ] Lighting is consistent: highlight on top, shadow below, on every button
- [ ] No button looks like plain text
- [ ] Hover gives a visible elastic overshoot (`~1.06` → `1.04`, `-2px` Y) and settles smoothly (`< 350ms`)
- [ ] Press gives a squash (`0.96, 0.97`) and rubber release
- [ ] Shadow blur and offset animate smoothly with lift (no snapping)
- [ ] Cards, labels, and stats remain flat (`L1`)
- [ ] Inputs/dropdowns look inset (`Bg.Inset`) and clearly distinct from raised buttons
- [ ] Selected sidebar item and selected mode pill look pressed-in
- [ ] `"Welcome back, JAMES"`, System Monitor labels, and all page headers/rows render without clipping or overlap
- [ ] No frame drops during live 1 Hz monitoring updates; no loops running on hidden/minimized pages
- [ ] Animations disabled cleanly when OS animation effects or in-app reduced motion are off
- [ ] Keyboard focus (`2px` cyan rim) and disabled states (`40%` opacity, no shadow) look correct
- [ ] Verified across all 11 views at `100%`, `125%`, and `150%` DPI
