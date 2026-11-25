# JamesOptimizer V3 — Feature Roadmap & Architecture Plan (v2)

**Target Solution:** `optimizer.slnx` (`JamesOptimizer.WinUI` + `JamesOptimizer.Service`)
**Baseline Runtime:** .NET 10, WinUI 3 (Windows App SDK 2.5.1), Win2D 1.4.0, Windows Worker Service
**Release Gate:** Every feature and refactor must pass all `[BLOCKER]` criteria in `goal.md` (100% Apply → Revert reversibility, strict IPC ACL/validation, zero AV false-positive patterns, native C# APIs over temp script drops).
**Explicit Exclusion:** Multi-language / localization is **out of scope**.

> **What changed in v2:** added a Phase 0.4 architecture foundation (declarative tweak catalog, crash-safe journal, OS-build gating), a Dry-Run/Preview mode, an AV-hygiene and code-signing track, tests/CI, and a new Phase 4 for high-value features (storage visualizer, driver-store/WinSxS cleanup, GPU/network adapter tweaks, health score). Verification moved to Phase 5. Each item now has an explicit risk level, priority and acceptance test.

---

## Legend

| Tag | Meaning |
|---|---|
| **P0 / P1 / P2** | Must-have / should-have / nice-to-have |
| 🟢 / 🟡 / 🔴 | Risk: safe & trivially reversible / reversible but system-wide / needs admin + reboot or hardware dependent |
| `[ADMIN]` | Needs elevation (UI must degrade gracefully when not elevated) |
| `[REBOOT]` | Takes effect after restart (UI must say so) |

---

## Phase 0: `goal.md` Compliance, Safety & Foundation

Eliminate release-gate blockers *and* build the foundation every later feature depends on.

### 0.1 Harden Service IPC & Background Maintenance (P0 · 🟡)
- **Files:** `JamesOptimizer.Service/Worker.cs`, `JamesOptimizer.WinUI/Services/BackgroundServiceClient.cs`
- **Changes:**
  - Replace `WellKnownSidType.AuthenticatedUserSid` in `ListenToNamedPipeAsync` with `BuiltinAdministratorsSid` / `LocalSystem` ACL for privileged commands and **explicitly deny `NetworkSid`** so remote SMB clients can never connect. (Do **not** set `PipeOptions.CurrentUserOnly`, because a `LocalSystem` service and an elevated admin user have different SIDs.)
  - Version handshake (`HELLO:<uiVersion>`), strict command allow-list, parameter range validation (`SET_TIMER` bounds, sanitized game list), **maximum message length and per-connection rate limit**.
  - Verify the connecting client **image path** (`GetNamedPipeClientProcessId` → confirm `JamesOptimizer.WinUI.exe` in install dir), plus **Authenticode signature verification gated behind `#if !DEBUG`** so unsigned local Debug builds still work while Release builds block foreign tools.
  - Structured logging (`ILogger` → rolling file in `%ProgramData%\JamesOptimizer\logs`) for every rejected command, with size cap and retention.
  - Remove `C:\Windows\Prefetch` deletion from `Worker.RunFastCleanAsync`.
- **Acceptance:** non-admin and network clients fail to connect on privileged commands; fuzzed/oversized/malformed commands are rejected and logged; service never crashes on bad input.

### 0.2 Eliminate AV-Triggering Script Drops & Unsafe Service Disabling (P0 · 🟡)
- **Files:** `Core/OptimizationEngine.cs`, `Resources/Windows11Debloater.ps1`, `Resources/Windows 10 Debloater.ps1`
- **Changes:**
  - Remove `SecurityHealthService` and `wscsvc` from `RunServiceDebloaterAsync` (violates `goal.md` §4). Add a **protected-services deny-list** constant (Defender `WinDefend`/`Sense`, `SecurityHealthService`, `wscsvc`, `mpssvc`, `BFE`, `EventLog`, `RpcSs`, `DcomLaunch`, `TrustedInstaller`, Windows Update core) enforced centrally so *no* code path can disable them.
  - Replace temp `.reg` / `.bat` / hidden `.ps1` (`ExecuteRegTweakAsync`, `ExecuteBatTweakAsync`, `RunWindowsDebloatAsync` with `-ExecutionPolicy Bypass`) with native `Microsoft.Win32.Registry`, `ServiceController`, `NetworkInterface` and P/Invoke.
  - Replace any remaining `Process.Start("cmd"/"powershell"/"reg"/"sc"/"netsh"/"bcdedit")` with native APIs; where unavoidable (`bcdedit`, `powercfg`, `fsutil`) call the **absolute System32 path**, no shell, no `-WindowStyle Hidden`, fixed argument arrays (no string concatenation → no injection).
  - Retire the embedded `.ps1` resources once Phase 2.1 ships.
- **Acceptance:** a runtime file-system monitor (Procmon) shows zero `.bat/.reg/.ps1` writes; static grep for `Bypass`, `Hidden`, `cmd /c` returns nothing.

### 0.3 Upgrade `TweakBackupService` to Full-Spectrum Snapshots (P0 · 🟡)
- **Files:** `Core/TweakBackupService.cs`, `MainWindow.xaml.cs`
- **Changes:**
  - Extend `TweakSnapshot` beyond registry values: Windows Service `StartMode` + running state, active `powercfg` GUID, `bcdedit` values, `HOSTS` hash/path, **scheduled-task enabled state, file/folder ACL changes, environment `PATH`, App Paths/context-menu keys (including "key did not exist" tombstones)**.
  - Batch/grouped snapshots: one logical tweak = one atomic entry in the `Ctrl+L` Audit Drawer.
  - **Record "value did not exist" vs "value was X"** so revert can delete a value you created instead of writing a bogus default.
  - Optional System Restore Point (`SRSetRestorePoint`) before the first system-wide apply; handle the 24-hour throttle gracefully.
  - **Versioned snapshot schema** (`schemaVersion` field + migrations) so V3.x updates never orphan older backups.
- **Acceptance:** multi-part tweak reverts atomically; reverting a tweak that created a key removes it.

### 0.4 Architecture Foundation (NEW · P0 · 🟢)
These make Phases 1–4 dramatically cheaper and safer.
- **Shared `JamesOptimizer.Core` Library** — extract shared core logic (`TweakDefinition` catalog, `TweakBackupService`, write-ahead journal, protected-services deny-list, shared logger, memory/cleanup helpers) into a `JamesOptimizer.Core` project referenced by both `JamesOptimizer.WinUI` and `JamesOptimizer.Service` so nothing is duplicated between UI and Service.
- **Declarative Tweak Catalog** — one `TweakDefinition` per tweak (JSON or C# records): `Id`, `Title`, `Description`, `Category`, `Risk`, `RequiresAdmin`, `RequiresReboot`, `MinOsBuild`/`MaxOsBuild`, `Detect()`, `Apply()`, `Revert()`, `Tags`, `LearnMoreUrl`. UI pages render from the catalog; no more per-page hardcoded toggles. This is what makes live-state detection (1.1), profile export (2.8) and the Apply→Revert test harness (Phase 5) generic.
- **Crash-safe Write-Ahead Journal** — write the intended revert data to disk *before* applying. If the app/PC crashes mid-apply, next launch detects an incomplete journal entry and offers **Roll Back / Finish**.
- **OS-build & edition gating** — Windows 10 vs 11 (22H2/23H2/24H2/25H2), Home vs Pro, ARM64 vs x64, laptop vs desktop, Modern Standby support. Tweaks that don't apply are hidden or marked "Not applicable" instead of silently failing.
- **Elevation & Pipe Boundary** — UI runs unelevated by default; read-only status/metrics work without elevation (separate read-only status query or unelevated local check), while admin-only actions trigger a single UAC-elevated apply/broker flow with a clear reason so `0.1`'s admin pipe ACL never locks out an unelevated UI. Never relaunch elevated just to show a page.
- **Dry-Run / Preview mode** — every Apply can show "this will change: `HKLM\...\X: 1 → 0`, service `SysMain: Auto → Disabled`" before committing. Reuse for profile import (2.8).
- **Panic Revert** — a visible "Undo everything JamesOptimizer changed" button *and* a Safe-Mode-friendly CLI (`JamesOptimizer.WinUI.exe --revert-all`) for when a tweak breaks boot/login.
- **Logging & diagnostics** — one logging pipeline shared by UI and Service; "Export diagnostic bundle" (logs + snapshot manifest, no personal data) for support.
- **Error contract** — every tweak returns `Success | AlreadyApplied | NotApplicable | NeedsAdmin | NeedsReboot | Failed(reason)`; the UI maps these to consistent badges.

### 0.5 Code Signing, Installer & AV Hygiene Track (NEW · P0 · 🟢)
- Sign `JamesOptimizer.WinUI.exe`, `JamesOptimizer.Service.exe` and the installer with an **EV/OV code-signing certificate** (or Azure Trusted Signing) + timestamping. Unsigned system-tweaking tools are the #1 cause of SmartScreen/AV flags.
- Embed full version info, manifest (`requestedExecutionLevel`), and avoid packers/obfuscators (already a goal).
- Submit each release to Microsoft Defender's false-positive portal *before* public launch; keep a short "AV FAQ" page.
- Service installed with a **restrictive ACL** (only Administrators can stop/reconfigure) and recovery actions (restart on failure).
- Uninstaller **offers to revert all tweaks** before removing the app and leaves no scheduled tasks, services, or registry keys.

**Done when:** no temp scripts are written, Defender/security services are untouchable by any code path, IPC rejects unauthorized callers/invalid commands, snapshots cover multi-part tweaks with crash-safe journaling, and signed builds pass a clean-VM install/uninstall.

---

## Phase 1: Surface Dormant Code & Fix Half-Wired Pages

### 1.1 Live State Detection & Real Revert in `PerformancePage` (P0 · 🟡)
- **Files:** `Views/PerformancePage.xaml(.cs)`, `Core/OptimizationEngine.cs`
- Probe real registry/service/powercfg state on `Page_Loaded` (via catalog `Detect()`), never hardcode `IsEnabled`.
- Route every `ApplyAsync(false)` through `TweakBackupService`; `BtnRevertAll` / `PresetStock` restore *original* values (not "Windows defaults" guessed from memory).
- Surface `RunEliteTweaksAsync` options (SSD `fsutil` 8dot3/lastaccess, `Disable-MMAgent -MemoryCompression`, BCD timer flags) as individual Advanced items with risk badges.
- **Add presets:** `Stock` · `Balanced` · `Gaming` · `Privacy` · `Laptop/Battery`, each previewable via Dry-Run.
- **Add "Drift detection":** if Windows Update or another tool reverts a tweak, show a "Reapply?" badge instead of silently displaying stale state.
- **Acceptance:** toggle state after app restart equals real OS state in 100% of tweaks.

### 1.2 Duplicate File Finder & Downloads Sweeper (`StoragePage`) (P1 · 🟢)
- Wire `ScanDuplicatesAsync` (size-group → **partial hash (first/last 64 KB) → full SHA-256**, so large drives scan fast) with progress, cancellation, "Keep Newest / Oldest / Shortest Path".
- Wire `ScanDownloadsAsync` (age > 30/90 days, size filters, file-type filters).
- Recycle Bin by default; permanent delete is opt-in.
- **Safety:** skip system folders, `OneDrive` placeholders (cloud-only files, to avoid triggering mass downloads), reparse points/junctions, and anything currently locked. Never auto-select *every* copy of a duplicate group.
- Add **Exclusion list** (folders/patterns) persisted in settings.

### 1.3 Expanded Startup & Autoruns Inspector (`StartupPage`) (P1 · 🟡)
- Wire `GetRegistryAutoruns()` (`Run`, `RunOnce`, `Policies\Explorer\Run`, `Winlogon`) plus **Scheduled Tasks** reader (non-Microsoft logon/boot tasks) via the Task Scheduler COM/`Microsoft.Win32.TaskScheduler` — not `schtasks` text parsing.
- Filter pills: `Apps` | `Scheduled Tasks` | `Registry Autoruns` | **`Services (3rd-party)`**.
- **Keep disable reversible** (move to the `StartupApproved` key / task disabled state, don't delete).
- Show publisher signature, file path, **startup impact (measured from Event ID 100–110 in `Microsoft-Windows-Diagnostics-Performance/Operational`, not just estimated)**, and a "Delay start" option.

### 1.4 Real Hardware SMART, Battery Health & Active Adapter Fixes (P1 · 🟢)
- Replace hardcoded `smartStatus = "OK · Healthy"` with `MSStorageDriver_FailurePredictStatus` / `Win32_DiskDrive.Status` **and NVMe health via `IOCTL_STORAGE_QUERY_PROPERTY` (temperature, wear %, available spare, media errors)**. Show "Unknown" honestly when a controller doesn't expose data.
- Battery Health: `BatteryFullChargedCapacity` vs `DesignCapacity`, cycle count where available (`IOCTL_BATTERY_QUERY_INFORMATION`).
- `NetworkPage.BtnSetDns_Click` → detect active adapters through `NetworkInterface.GetAllNetworkInterfaces()` (up, non-loopback, non-virtual/VPN) instead of hardcoded `"Wi-Fi"`/`"Ethernet"`. **Let the user choose which adapter**, default to the one with the default gateway.
- Replace placeholder `https://discord.gg/invite` in `SettingsPage.xaml.cs` with a valid link or remove it.
- **Acceptance:** no hardcoded "healthy" values remain; a failing/unsupported source displays "Unavailable" rather than a fake OK.

**Done when:** every method in `OptimizationEngine` and `StartupManagerService` has a working UI surface and `DiagnosticsPage` shows real telemetry.

---

## Phase 2: Features Extracted from `hellzerg/optimizer` (Native & Safe)

> **License note:** check `hellzerg/optimizer`'s license before copying *any* code. Re-implement ideas natively rather than porting source, and credit the project in an About/Acknowledgements page.

### 2.1 Native UWP / Appx Bloatware Manager (P0 · 🟡)
- New `Core/AppxManagerService.cs`; UI in `CleanupsPage` or a dedicated `AppsPage`.
- `Windows.Management.Deployment.PackageManager` for installed + provisioned packages (provisioned removal needs the DISM/`Dism API` path, `[ADMIN]`).
- Categories: `Safe to Remove` · `Xbox & Gaming` · `Recommended to Keep` · `Never Remove`. Hard-protect `Microsoft.WindowsStore`, `DesktopAppInstaller`, `VCLibs`, `UI.Xaml`, `NET.Native`, `WindowsTerminal`-class system dependencies and **anything another installed package depends on** (check `PackageDependencies`).
- Pre-remove **record package full name + Store ID** so "Reinstall" can use `winget`/Store deep link. Warn clearly that some inbox apps return after major feature updates.
- Multi-select uninstall with Dry-Run summary.

### 2.2 Modern Windows 11, AI & Office Privacy Policies (P1 · 🟡)
- Backed by `TweakBackupService` and the catalog:
  - Windows **Copilot** (OS policy + Edge sidebar/Copilot) and **Recall** (24H2+ Copilot+ PCs; gate by build).
  - Office 2016+/365 telemetry (`...\Office\16.0\osm`, CEIP).
  - OneDrive sync/autorun policy (no file deletion; show *"files are not removed"* message).
  - Taskbar clutter: Search box/highlights, Widgets, Chat/Meet Now, lock-screen tips/ads, **Start menu recommendations/ads, "Suggested" apps, Settings-page promotions**.
  - **UTC hardware clock (`RealTimeIsUniversal=1`)** for dual-boot.
  - **Modern Standby / Network-in-Standby** with hardware compatibility check (`powercfg /a` equivalent): **disable/hide** disabling Modern Standby when firmware reports S3 sleep is unavailable so sleep/wake cannot break.
  - **Telemetry level** (Security/Basic) with honest labelling: Home/Pro can't fully disable; don't claim it does.
- Every item shows what it changes (registry path/policy name) in an expandable "Details" row.

### 2.3 Multi-Browser & Multi-Profile Cache Cleaner (P1 · 🟢)
- Auto-discover profiles (`Default`, `Profile 1..N`) for Edge, Chrome, Brave, Opera/Opera GX, Vivaldi, Firefox (`cache2`).
- Target safe caches (`Cache`, `Code Cache`, `GPUCache`, `Service Worker\CacheStorage`, `Crashpad\reports`); never touch passwords, bookmarks, cookies unless the separate "Cookies / Session" checkbox is ticked.
- **Detect running browsers** and prompt to close (or skip locked files) rather than failing silently.
- Show size per browser/profile **before** cleaning; log bytes freed.
- Surface `RunShaderClearAsync` (D3D, NVIDIA `GLCache`/`DXCache`, AMD `DxCache`) as its own toggle. *Warn that first game launch after clearing will stutter while shaders rebuild.* Never call `cleanmgr /sagerun:64`.

### 2.4 File Lock / Handle Finder & Unlocker (P1 · 🟡)
- New `Core/FileLockService.cs`; UI in `ToolboxPage` + quick-link from `StoragePage`.
- Restart Manager API (`RmStartSession`, `RmRegisterResources`, `RmGetList`, `RmEndSession`); show PID, name, path, lock type, **service name and whether it's a critical/restartable process** (`RmShutdown` as a gentler option than kill).
- Protect critical processes (`csrss`, `lsass`, `System`, `wininit`, `winlogon`, `services`, `smss`) from termination — hard block, not just a confirmation.
- Add **Explorer context-menu entry "What's locking this?"** (reuses 2.6 integrator).
- Actions: Close gracefully → Terminate (confirmation) → Retry Delete / Recycle / **Schedule delete on reboot** (`MoveFileEx` with `MOVEFILE_DELAY_UNTIL_REBOOT`).

### 2.5 `HOSTS` File Editor & User Block Management (P1 · 🟡)
- New `Core/HostsManagerService.cs`; panel in `NetworkPage`.
- Structured rows (`IP`, `Hostname`, `Comment`, `Enabled`), IP/domain validation, grouped/tagged user-defined or imported blocks wrapped in `# BEGIN JamesOptimizer:<tag>` / `# END` markers for one-click clean removal.
- Timestamped backup before every write, 1-click **Restore Original**, then `DnsFlushResolverCache` (P/Invoke `dnsapi.dll`, no `ipconfig` spawn).
- **Safety:** ship **zero bundled Microsoft/telemetry domain blocklists** inside the binary/repo (prevents Defender `HostsFileHijack` false positives per `goal.md` §4); handle Defender's HOSTS-file protection cleanly (detect and explain instead of fighting it); warn users before adding entries for Windows Update/activation/Store domains or very large blocklists that slow DNS resolution.
- Preserve existing comments, line endings and encoding (UTF-8 without BOM / ANSI).

### 2.6 Windows Environment Integrator (`Win+R` Aliases, Context Menu & `PATH`) (P2 · 🟡)
- New `Core/IntegratorService.cs`; UI in `ToolboxPage`.
- `Win+R` aliases via `HKLM\...\App Paths\<alias>.exe` (validate alias isn't an existing system command; block overriding `cmd`, `powershell`, `regedit`, etc. — **alias hijacking is both a security and AV concern**).
- Desktop/Background context-menu entries with icon + command; **only allow commands from files the user picked, store the exact command line in the snapshot**.
- `PATH` Inspector: HKLM + HKCU, highlight dead directories, duplicates, expandable `%VAR%` entries (preserve `REG_EXPAND_SZ`!), edit with automatic backup and broadcast `WM_SETTINGCHANGE`.

### 2.7 `winget` Essentials Installer (P2 · 🟢)
- New `Core/WingetInstallerService.cs`; catalog card in `ToolboxPage`.
- Curated catalog (7-Zip, Notepad++, Everything, CPU-Z, GPU-Z, WizTree, browsers, VC++ Redistributables, .NET Desktop Runtimes) stored as an editable JSON file.
- `winget list` / `install --id --exact --accept-*` with fixed argument arrays and **`--source winget`** pinned. Parse `--output`/exit codes rather than screen-scraping text where possible.
- Detect that `winget` is missing and offer to install App Installer from the Store instead of failing.
- Show per-package progress, retry, and exit-code explanations; add **"Upgrade all"** with checkbox exclusions.

### 2.8 JSON Profile Export / Import & Headless CLI Mode (P1 · 🟢)
- New `Core/ProfileConfigService.cs`, `App.xaml.cs`, `SettingsPage`.
- Export tweak states (by catalog `Id`, so profiles survive UI changes), DNS, power plan, HOSTS flags, service prefs.
- Import → **Dry-Run diff** ("12 changes; backup will be saved") → Apply.
- CLI: `--apply <profile.json> --silent`, **`--dry-run`, `--revert-last`, `--revert-all`, `--export <path>`**, with documented exit codes and log path. Reject profiles with unknown/unsafe ids; **profiles may only reference catalog tweaks, never arbitrary commands or file paths** (prevents a shared profile from being a malware vector).
- Add `schemaVersion` + optional signature/hash for shared community profiles.

**Done when:** all 8 features run natively with full revert/backup support, zero PowerShell/batch dependencies, and are reachable through both UI and (where applicable) CLI.

---

## Phase 3: Live Daemon & Telemetry Features (Beyond `hellzerg/optimizer`)

### 3.1 Threshold-Based Smart Standby RAM Purger (P1 · 🟡 `[ADMIN]`)
- `Worker.cs`, `DashboardPage`, `SettingsPage`.
- Memory Threshold Mode using `GlobalMemoryStatusEx` + `NtQuerySystemInformation(SystemMemoryListInformation)`; purge via `NtSetSystemInformation` only when `free < threshold` **and** `standby > minimum`, with cooldown and **a max purges-per-hour cap**.
- Pause purging while a game/benchmark is running if the user selected "don't touch during fullscreen" (`SHQueryUserNotificationState`).
- Dashboard: live Standby / Modified / Free / Committed breakdown with a Win2D sparkline. Log each purge (before/after MB) so users can see if it's actually helping — **if it isn't, say so** (honesty builds trust and avoids placebo-tool reputation).
- Clearly note in UI that the standby list is normally beneficial, and the feature targets specific stutter scenarios.

### 3.2 Per-Game Automation Profiles (P1 · 🟡)
- Structured rules (`ProcessName`, `PowerPlan`, `ProcessPriority`, `DisableEfficiencyMode`, `PurgeStandbyOnLaunch`, `TemporarilySuspendServices`, **`CpuAffinity`, `GpuPreference`, `Notifications (Focus Assist) on/off`**).
- Detect launches with **WMI/ETW process-start events** instead of polling where possible (lower idle CPU).
- Record previous state in the journal (0.4) so **a crash or service restart mid-game still restores power plan/services on next start**.
- **Emulator-aware templates** (BlueStacks, MSI App Player, LDPlayer, etc.) alongside native-game templates.
- Never suspend protected services (0.2 deny-list); never raise priority above `High` (no `Realtime`).
- Import/export profiles through 2.8.

### 3.3 DNS Benchmark & Active Socket/Port Inspector (P2 · 🟢)
- Presets: Cloudflare, Google, Quad9, OpenDNS, AdGuard, NextDNS; user-defined custom servers (IPv4 + IPv6).
- Live benchmark with real UDP (and optionally DoH) queries against **cached + uncached domains**, N repetitions → median, p95 and jitter; show packet loss; recommend but never auto-switch.
- Support **DNS-over-HTTPS** setting via the native Windows 11 DoH template when available.
- Active Connections / Listening Ports (`GetExtendedTcpTable`/`GetExtendedUdpTable` for PID mapping, which `IPGlobalProperties` lacks), with per-process **bytes/s via ETW** (`Microsoft-Windows-Kernel-Network`, using a deterministic session name that stops any stale session on startup and disposes cleanly on page unload/exit) so "bandwidth hogs" are truly measured; "Open file location" and "Suspend/End" actions with critical-process protection.

### 3.4 Benchmark History & Before/After Delta Reporting (P2 · 🟢)
- Persist timestamped `BenchmarkSystemAsync` runs in `%LocalApplicationData%\JamesOptimizer\benchmarks.json` **including system context** (power plan, thermals/temperature if available, background CPU load, Windows build) so deltas aren't misleading.
- Run N iterations, discard the first warm-up, report median ± variance; refuse to compare runs taken under different power plans without a warning.
- Baseline vs Latest delta badge in `ToolboxPage`; embed table in exported HTML report. Add **PDF/CSV export** (reuse `OptimizationReportGenerator`).

**Done when:** the service performs threshold RAM hygiene and per-game automation robustly, DNS benchmarks use real resolution, and benchmark history persists with trustworthy deltas.

---

## Phase 4: New High-Value Features (NEW)

Ordered by value ÷ effort. Each is native C#, snapshot-backed where it changes state.

### 4.1 Storage Visualizer / Large-File Finder (P1 · 🟢)
- WizTree-style treemap rendered with Win2D. For NTFS, scan via **MFT/USN journal (`[ADMIN]`)** for instant results, falling back to `FindFirstFileEx` for non-admin/non-NTFS.
- Drill-down, "Largest files", "Oldest files", and quick-actions (open location, recycle).

### 4.2 Deep System Cleanup (P1 · 🟡 `[ADMIN]`)
- **WinSxS component cleanup** via the DISM API (`DismApi.dll`), `SoftwareDistribution\Download` (stop/start `wuauserv` safely and restore its previous state), Delivery Optimization cache, Windows Error Reporting dumps, old `Windows.old` (with explicit "you can no longer roll back" warning), Windows upgrade logs.
- **Driver Store cleaner**: enumerate stale OEM driver packages (SetupAPI / `pnputil` equivalent), group by device, **always keep the newest + currently-installed version**; show size.
- Every category shows reclaimable size *before* deleting; none of this runs on the background timer.

### 4.3 GPU, Display & Network-Adapter Tweaks (P2 · 🟡)
- Windows Game Mode, **Hardware-accelerated GPU Scheduling (HAGS)** `[REBOOT]`, MPO toggle, per-app GPU preference (`UserGpuPreferences`), Variable Refresh Rate / windowed-game optimizations.
- Network adapter: read-only inspector first (link speed, power-saving, RSS, offloads, Nagle/TCP ack frequency, interrupt moderation) → then offer *only* well-evidenced toggles. Avoid "magic" registry tweaks with no measurable benefit; each tweak carries an **evidence rating** (Measured / Situational / Placebo) and a short note.
- **Hardware-compat guard:** hide NVIDIA/AMD-only options when the hardware isn't present.

### 4.4 System Health Score & Recommendations (P1 · 🟢)
- Dashboard score from real signals: free disk space, SMART/NVMe health, startup count/impact, pending reboot, Windows Update age, RAM pressure, thermals, power plan, unneeded background apps.
- "Fix" buttons route to the right page with a pre-selected Dry-Run. Show **"nothing to fix"** honestly when the PC is fine.

### 4.5 Scheduled Maintenance & Reports (P2 · 🟡)
- Replace fixed `AutoTimer` with a **user-defined schedule** (weekly cleanup, monthly report) executed by the service; skip when on battery, in a fullscreen game, or user active.
- Toast notification summary ("Freed 3.2 GB, 2 startup items flagged") with Undo.

### 4.6 Privacy Dashboard (P2 · 🟢)
- Single view of Windows privacy toggles (advertising ID, activity history, diagnostic data, location, clipboard history, tailored experiences) with current state and one-click revert.

### 4.7 Self-Update & Release Channel (P1 · 🟡)
- Signed update manifest + hash check, `Stable` / `Beta` channels, delta-friendly installer, **automatic rollback if the new build fails to start**.
- **Opt-in / toggleable** update checks in Settings (`goal.md` §4: zero hidden network calls by default); if crash reporting is added it is **opt-in**, local-first, and documented.

### 4.8 Accessibility, Polish & Onboarding (P2 · 🟢)
- Keyboard navigation + Narrator labels across all WinUI pages (localization stays out of scope, accessibility does not), high-contrast theme check, DPI scaling at 100–250%.
- First-run wizard: pick profile (Gaming / Work / Laptop), create restore point, preview changes, apply.
- Contextual "Why does this matter?" text on each tweak (pulled from the catalog description).

---

## Phase 5: Verification & Release Gate (`goal.md`)

1. **Build & Static Analysis:** `dotnet build optimizer.slnx -c Release`, `Installer\Build-Installer.ps1`, analyzers on, **warnings-as-errors** for security rules (CA2100, CA3075, SYSLIB — including migrating `X509Certificate.CreateFromSignedFile` in `ServiceManagerLogic.cs` / `StartupManagerService.cs` to `X509CertificateLoader.LoadCertificateFromFile`), nullable enabled.
2. **Automated Apply → Revert Parity Harness:** because tweaks live in the catalog (0.4), a generic test iterates every tweak: snapshot full OS state (registry hive diff, services, powercfg, bcdedit, HOSTS, tasks) → Apply → verify → Revert → **assert diff vs. original is empty**. Runs in a Hyper-V/Windows Sandbox VM on CI.
3. **Unit & Integration Tests:** parsers (HOSTS, PATH, profile JSON), validators (IPC commands, aliases), and the protected-process/service deny-lists get dedicated tests; include **negative tests** that attempt to disable `WinDefend`, kill `lsass`, or send oversized IPC messages.
4. **Crash-Recovery Test:** kill the app/service mid-apply and mid-game-profile; verify the journal rolls back or resumes cleanly on next launch.
5. **IPC & Stability Soak:** non-admin and network clients rejected on `\\.\pipe\JamesOptimizerPipe`; invalid commands logged; 30-minute UI + Service soak with < 5% memory growth and **zero handle/GDI leak growth** (Win2D).
6. **Compatibility Matrix:** Windows 10 22H2, Windows 11 23H2/24H2/25H2, x64 + ARM64, Home/Pro, laptop/desktop, non-admin user, high-DPI.
7. **AV & Installer Hygiene:** signed binaries scanned by Defender + VirusTotal (0 detections, no packers, no temp script drops); clean-VM install → run → revert → uninstall leaves no residue (files, services, tasks, registry).
8. **Release Checklist:** changelog, version bump, SHA-256 of installer published, Defender false-positive submission done, restore-point behavior documented, known-issues list reviewed.

---

## Suggested Build Order

| Order | Work | Why first |
|---|---|---|
| 1 | 0.1, 0.2 | Release blockers & security |
| 2 | 0.4 (catalog + journal + dry-run) | Everything else builds on it |
| 3 | 0.3, 0.5 | Full-spectrum reversibility + signing |
| 4 | 1.1, 1.3, 1.4 | Make existing pages truthful |
| 5 | 2.1, 2.8, 2.5 | Highest user value, replaces scripts |
| 6 | 1.2, 2.3, 4.1, 4.2 | Storage/cleanup wins users see immediately |
| 7 | 3.1, 3.2 | Differentiating daemon features |
| 8 | 2.2, 2.4, 2.6, 2.7, 3.3, 3.4, 4.x | Remaining polish and extras |
| 9 | Phase 5 gate (run continuously from step 2 onward) | Don't leave testing to the end |

---

## Risk Register

| Risk | Mitigation |
|---|---|
| AV/SmartScreen flags the optimizer | Code signing, no scripts/packers, FP submissions, absolute-path native calls |
| A tweak breaks boot/login | Journal + Panic Revert + `--revert-all` from Safe Mode + restore point |
| Windows Update undoes tweaks | Drift detection and "Reapply" badge |
| Malicious shared profile | Catalog-id-only profiles, schema validation, optional hash/signature |
| Placebo tweaks hurt credibility | Evidence ratings, measured before/after deltas, honest "no change" reporting |
| Service becomes a privilege-escalation vector | Admin-only ACL, signed-client check, allow-list, rate limits, no arbitrary command execution |
| OS updates change registry/API behavior | Build gating + periodic compatibility matrix runs |
| Licensing of ideas from `hellzerg/optimizer` | Re-implement natively, verify license, credit in About page |

---

## Explicitly Out of Scope
- Multi-language / localization
- Disabling Defender, Firewall, Windows Update or Security Center (not now, not behind an "advanced" toggle)
- Registry "cleaners", RAM "boosters" that only allocate/free memory, and other placebo features
- Downloading third-party binaries from hardcoded URLs