using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace JamesOptimizer.Core;

public enum TweakRisk
{
    Safe,
    SystemWide,
    RebootRequired
}

public enum EvidenceRating
{
    Measured,
    Situational,
    Placebo
}

public enum TweakStatus
{
    Success,
    AlreadyApplied,
    NotApplicable,
    NeedsAdmin,
    NeedsReboot,
    Failed
}

public sealed record TweakChangeItem(string Target, string Before, string After);

public sealed record TweakExecutionResult(
    TweakStatus Status,
    string Message,
    IReadOnlyList<TweakChangeItem>? Changes = null);

public sealed record TweakDetectState(
    bool IsApplied,
    bool IsApplicable,
    string? NotApplicableReason = null,
    bool IsDrifted = false);

public sealed class TweakDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public string IconGlyph { get; set; } = "\uE770";
    public string ImpactText { get; set; } = "HIGH IMPACT";
    public TweakRisk Risk { get; set; } = TweakRisk.Safe;
    public EvidenceRating Evidence { get; set; } = EvidenceRating.Measured;
    public bool RequiresAdmin { get; set; }
    public bool RequiresReboot { get; set; }
    public int MinOsBuild { get; set; } = 0;
    public int MaxOsBuild { get; set; } = int.MaxValue;
    public bool RequiresLaptop { get; set; }
    public bool RequiresS3Sleep { get; set; }
    public GpuVendor? RequiredGpuVendor { get; set; }
    public string TargetDisplay { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string LearnMoreUrl { get; set; } = "https://github.com/jamesgod1461/JamesOptimizer";

    public Func<TweakDetectState> Detect { get; set; } = () => new TweakDetectState(false, false);
    public Func<IReadOnlyList<TweakChangeItem>> Preview { get; set; } = () => Array.Empty<TweakChangeItem>();
    public Func<TweakExecutionResult> Apply { get; set; } = () => new TweakExecutionResult(TweakStatus.Failed, "Not implemented");
    public Func<TweakExecutionResult>? Revert { get; set; }

    public string RiskLabel => Risk switch
    {
        TweakRisk.Safe => "SAFE",
        TweakRisk.SystemWide => "SYSTEM-WIDE",
        TweakRisk.RebootRequired => "REBOOT",
        _ => "SAFE"
    };

    public string EvidenceLabel => Evidence switch
    {
        EvidenceRating.Measured => "MEASURED",
        EvidenceRating.Situational => "SITUATIONAL",
        EvidenceRating.Placebo => "PLACEBO",
        _ => "MEASURED"
    };
}

[SupportedOSPlatform("windows")]
public static class TweakCatalog
{
    private static readonly Lazy<IReadOnlyList<TweakDefinition>> _lazyAll = new(BuildCatalog);
    public static IReadOnlyList<TweakDefinition> All => _lazyAll.Value;

    public static TweakDefinition? Find(string id) =>
        All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static (bool Applicable, string? Reason) CheckCompatibility(TweakDefinition def, OsEnvironmentInfo? env = null)
    {
        env ??= OsEnvironment.Detect();
        if (env.BuildNumber < def.MinOsBuild)
            return (false, $"Requires Windows build {def.MinOsBuild}+ (current {env.BuildNumber}).");
        if (env.BuildNumber > def.MaxOsBuild)
            return (false, $"Not applicable above Windows build {def.MaxOsBuild}.");
        if (def.RequiresLaptop && !env.HasBattery)
            return (false, "Laptop/battery hardware not detected.");
        if (def.RequiresS3Sleep && !env.SupportsS3Sleep)
            return (false, "Firmware does not expose S3 sleep (powercfg /a); blocking Modern Standby change to protect sleep/wake.");
        if (def.RequiredGpuVendor.HasValue && env.PrimaryGpuVendor != def.RequiredGpuVendor.Value)
            return (false, $"Requires {def.RequiredGpuVendor.Value} GPU.");
        return (true, null);
    }

    public static TweakExecutionResult ExecuteDryRun(string tweakId)
    {
        var def = Find(tweakId);
        if (def == null) return new TweakExecutionResult(TweakStatus.Failed, $"Unknown tweak '{tweakId}'.");
        var (ok, reason) = CheckCompatibility(def);
        if (!ok) return new TweakExecutionResult(TweakStatus.NotApplicable, reason ?? "Not applicable.");
        return new TweakExecutionResult(TweakStatus.Success, "Dry-run preview generated.", def.Preview());
    }

    public static TweakExecutionResult ApplyTweak(string tweakId)
    {
        var def = Find(tweakId);
        if (def == null) return new TweakExecutionResult(TweakStatus.Failed, $"Unknown tweak '{tweakId}'.");

        var (ok, reason) = CheckCompatibility(def);
        if (!ok) return new TweakExecutionResult(TweakStatus.NotApplicable, reason ?? "Not applicable.");

        var env = OsEnvironment.Detect();
        if (def.RequiresAdmin && !env.IsElevated)
            return new TweakExecutionResult(TweakStatus.NeedsAdmin, $"'{def.Title}' requires Administrator privileges.");

        var state = def.Detect();
        if (state.IsApplied)
            return new TweakExecutionResult(TweakStatus.AlreadyApplied, $"'{def.Title}' is already applied.");

        try
        {
            var result = def.Apply();
            if (result.Status == TweakStatus.Success && def.RequiresReboot)
                return result with { Status = TweakStatus.NeedsReboot, Message = $"{result.Message} (Reboot required to take full effect.)" };
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return new TweakExecutionResult(TweakStatus.NeedsAdmin, $"Administrator permissions denied while applying '{def.Title}'.");
        }
        catch (Exception ex)
        {
            SharedLogger.Error("TweakCatalog", $"Apply failed for {tweakId}", ex);
            return new TweakExecutionResult(TweakStatus.Failed, ex.Message);
        }
    }

    public static TweakExecutionResult RevertTweak(string tweakId)
    {
        var def = Find(tweakId);
        if (def == null) return new TweakExecutionResult(TweakStatus.Failed, $"Unknown tweak '{tweakId}'.");

        var env = OsEnvironment.Detect();
        if (def.RequiresAdmin && !env.IsElevated)
            return new TweakExecutionResult(TweakStatus.NeedsAdmin, $"Reverting '{def.Title}' requires Administrator privileges.");

        try
        {
            if (def.Revert != null)
                return def.Revert();

            if (TweakBackupService.RevertTweakById(def.Id, out string msg))
                return new TweakExecutionResult(def.RequiresReboot ? TweakStatus.NeedsReboot : TweakStatus.Success, msg);

            return new TweakExecutionResult(TweakStatus.Failed, msg);
        }
        catch (UnauthorizedAccessException)
        {
            return new TweakExecutionResult(TweakStatus.NeedsAdmin, $"Administrator permissions denied while reverting '{def.Title}'.");
        }
        catch (Exception ex)
        {
            SharedLogger.Error("TweakCatalog", $"Revert failed for {tweakId}", ex);
            return new TweakExecutionResult(TweakStatus.Failed, ex.Message);
        }
    }

    #region Helpers for Registry & Service Batching

    private static TweakDefinition MakeRegistryTweak(
        string id, string title, string desc, string category, string categoryLabel,
        string iconGlyph, string impact, TweakRisk risk, EvidenceRating evidence,
        bool requiresAdmin, bool requiresReboot, int minBuild,
        (string Path, string Name, object Value, RegistryValueKind Kind)[] ops,
        string[]? tags = null, bool requiresLaptop = false, bool requiresS3 = false, GpuVendor? gpu = null)
    {
        string targetDisplay = ops.Length > 0 ? $"{ops[0].Path} ({ops[0].Name})" : id;

        return new TweakDefinition
        {
            Id = id,
            Title = title,
            Description = desc,
            Category = category,
            CategoryLabel = categoryLabel,
            IconGlyph = iconGlyph,
            ImpactText = impact,
            Risk = risk,
            Evidence = evidence,
            RequiresAdmin = requiresAdmin,
            RequiresReboot = requiresReboot,
            MinOsBuild = minBuild,
            RequiresLaptop = requiresLaptop,
            RequiresS3Sleep = requiresS3,
            RequiredGpuVendor = gpu,
            TargetDisplay = targetDisplay,
            Tags = tags ?? Array.Empty<string>(),
            Detect = () =>
            {
                var (ok, reason) = CheckCompatibilityByParams(minBuild, requiresLaptop, requiresS3, gpu);
                if (!ok) return new TweakDetectState(false, false, reason);

                bool allMatch = true;
                foreach (var op in ops)
                {
                    object? cur = SafeGetRegValue(op.Path, op.Name);
                    if (!ValuesEqual(cur, op.Value, op.Kind))
                    {
                        allMatch = false;
                        break;
                    }
                }

                var activeSnap = TweakBackupService.GetLatestActiveForTweak(id);
                bool drifted = activeSnap != null && !allMatch;
                return new TweakDetectState(allMatch, true, null, drifted);
            },
            Preview = () =>
            {
                var list = new List<TweakChangeItem>();
                foreach (var op in ops)
                {
                    object? cur = SafeGetRegValue(op.Path, op.Name);
                    list.Add(new TweakChangeItem($"{op.Path}\\{op.Name}", cur?.ToString() ?? "<absent>", op.Value.ToString() ?? ""));
                }
                return list;
            },
            Apply = () =>
            {
                var rawChanges = TweakBackupService.ApplyMultiRegistryWithBackup(category, title, id, ops);
                var changes = rawChanges.Select(c => new TweakChangeItem(c.Target, c.Before, c.After)).ToList();
                return new TweakExecutionResult(TweakStatus.Success, $"Applied '{title}' cleanly.", changes);
            }
        };
    }

    private static (bool Ok, string? Reason) CheckCompatibilityByParams(int minBuild, bool laptop, bool s3, GpuVendor? gpu)
    {
        var env = OsEnvironment.Detect();
        if (env.BuildNumber < minBuild)
            return (false, $"Requires Windows build {minBuild}+.");
        if (laptop && !env.HasBattery)
            return (false, "Battery/laptop hardware not detected.");
        if (s3 && !env.SupportsS3Sleep)
            return (false, "Firmware lacks S3 sleep support.");
        if (gpu.HasValue && env.PrimaryGpuVendor != gpu.Value)
            return (false, $"Requires {gpu.Value} GPU.");
        return (true, null);
    }

    private static object? SafeGetRegValue(string path, string name)
    {
        try { return Registry.GetValue(path, name, null); }
        catch { return null; }
    }

    private static bool ValuesEqual(object? current, object target, RegistryValueKind kind)
    {
        if (current == null) return false;
        try
        {
            if (kind == RegistryValueKind.DWord)
            {
                // BUG FIX: Convert.ToInt32 throws for non-numeric/out-of-range values; use safe parse.
                int t = Convert.ToInt32(target);
                int c = current is int i ? i : (int.TryParse(current.ToString(), out int p) ? p : -1);
                return c == t;
            }
            if (kind == RegistryValueKind.QWord)
            {
                long t = Convert.ToInt64(target);
                long c = current is long l ? l : (long.TryParse(current.ToString(), out long p) ? p : -1L);
                return c == t;
            }
        }
        catch { return false; }
        return string.Equals(current.ToString(), target.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetActivePowerSchemeGuid()
    {
        var res = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/getactivescheme" });
        var m = Regex.Match(res.StdOut, @"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}");
        return m.Success ? m.Value : CoreConstants.BalancedGuid;
    }

    // BUG FIX: Return null when a service is absent on the current Windows SKU so snapshots never
    // record a phantom WindowsService step that would cause Revert to fail on sc.exe config (exit 1060).
    public static SnapshotStep? TryCaptureServiceStep(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            bool running = sc.Status == ServiceControllerStatus.Running;
            string startMode = sc.StartType.ToString();
            return new SnapshotStep
            {
                Kind = SnapshotStepKind.WindowsService,
                ServiceName = serviceName,
                OriginalStartMode = startMode,
                OriginalRunning = running
            };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    #endregion

    private static IReadOnlyList<TweakDefinition> BuildCatalog()
    {
        var list = new List<TweakDefinition>();

        // 1. Ultimate Performance Power Plan
        list.Add(BuildPowerPlanTweak());

        // 2. CPU Core Parking
        list.Add(BuildCoreParkingTweak());

        // 3. System Responsiveness QoS
        list.Add(MakeRegistryTweak(
            "system_responsiveness",
            "System Responsiveness Multimedia QoS",
            "Sets SystemResponsiveness to 0 in MMCSS SystemProfile, prioritizing foreground scheduling.",
            "LATENCY", "Latency & CPU", "\uE9D9", "CRITICAL", TweakRisk.Safe, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: false, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", (object)0, RegistryValueKind.DWord),
                (@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", (object)0x26, RegistryValueKind.DWord)
            },
            tags: new[] { "mmcss", "qos", "priority" }));

        // 4. Network Throttling Index
        list.Add(MakeRegistryTweak(
            "network_throttling",
            "Disable Network Throttling Index",
            "Sets NetworkThrottlingIndex to 0xFFFFFFFF to stop multimedia packet rate limiting.",
            "LATENCY", "Latency & CPU", "\uE839", "ZERO JITTER", TweakRisk.Safe, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: false, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", (object)unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord)
            },
            tags: new[] { "network", "throttling", "mmcss" }));

        // 5. Raw Mouse Aim
        list.Add(MakeRegistryTweak(
            "mouse_fix",
            "1:1 Raw Mouse Precision",
            "Disables Enhanced Pointer Precision acceleration curves for linear 1:1 sensor tracking.",
            "GAMING", "Gaming & Input", "\uE962", "1:1 RAW AIM", TweakRisk.Safe, EvidenceRating.Measured,
            requiresAdmin: false, requiresReboot: false, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseSpeed", (object)"0", RegistryValueKind.String),
                (@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseThreshold1", (object)"0", RegistryValueKind.String),
                (@"HKEY_CURRENT_USER\Control Panel\Mouse", "MouseThreshold2", (object)"0", RegistryValueKind.String)
            },
            tags: new[] { "mouse", "aim", "input" }));

        // 6. Keyboard Repeat Delay
        list.Add(MakeRegistryTweak(
            "keyboard_fix",
            "Keyboard Input Delay & Repeat Rate",
            "Sets KeyboardDelay to 0 and maximizes KeyboardSpeed to 31.",
            "GAMING", "Gaming & Input", "\uE765", "INPUT BUFFER", TweakRisk.Safe, EvidenceRating.Measured,
            requiresAdmin: false, requiresReboot: false, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_CURRENT_USER\Control Panel\Keyboard", "KeyboardDelay", (object)"0", RegistryValueKind.String),
                (@"HKEY_CURRENT_USER\Control Panel\Keyboard", "KeyboardSpeed", (object)"31", RegistryValueKind.String)
            },
            tags: new[] { "keyboard", "input", "repeat" }));

        // 7. HAGS
        list.Add(MakeRegistryTweak(
            "gpu_pipeline",
            "Hardware-Accelerated GPU Scheduling (HAGS)",
            "Offloads high-frequency GPU frame scheduling to the GPU's dedicated scheduler (requires reboot).",
            "DISPLAY", "GPU & Display", "\uE7F4", "RENDER LAG", TweakRisk.RebootRequired, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: true, minBuild: 19041,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", (object)2, RegistryValueKind.DWord)
            },
            tags: new[] { "gpu", "hags", "scheduling" }));

        // 8. MPO Toggle
        list.Add(MakeRegistryTweak(
            "mpo_disable",
            "Disable Multi-Plane Overlay (MPO)",
            "Disables DWM Multi-Plane Overlay to mitigate flickering or black-screen stutters on multi-monitor/VRR setups.",
            "DISPLAY", "GPU & Display", "\uE7F4", "STABILITY", TweakRisk.RebootRequired, EvidenceRating.Situational,
            requiresAdmin: true, requiresReboot: true, minBuild: 19041,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", (object)5, RegistryValueKind.DWord)
            },
            tags: new[] { "mpo", "dwm", "flicker" }));

        // 9. Windows Game Mode
        list.Add(MakeRegistryTweak(
            "game_mode",
            "Windows Game Mode",
            "Enables native Windows Game Mode to suppress background update restarts and prioritize game frame pacing.",
            "GAMING", "Gaming & Input", "\uE7FC", "FRAME PACING", TweakRisk.Safe, EvidenceRating.Measured,
            requiresAdmin: false, requiresReboot: false, minBuild: 18362,
            ops: new[]
            {
                (@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AutoGameModeEnabled", (object)1, RegistryValueKind.DWord)
            },
            tags: new[] { "gamemode", "gaming" }));

        // 10. Windowed Game Optimizations (Win11 22H2+)
        list.Add(MakeRegistryTweak(
            "windowed_game_opt",
            "Optimizations for Windowed Games",
            "Upgrades DX10/11 presentation to flip-model swapchains for lower latency and AutoHDR/VRR in borderless games.",
            "DISPLAY", "GPU & Display", "\uE7F4", "VRR / FLIP", TweakRisk.Safe, EvidenceRating.Measured,
            requiresAdmin: false, requiresReboot: false, minBuild: 22621,
            ops: new[]
            {
                (@"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", (object)"SwapEffectUpgradeEnable=1;", RegistryValueKind.String)
            },
            tags: new[] { "vrr", "dx11", "windowed" }));

        // 11. Disable Game DVR
        list.Add(MakeRegistryTweak(
            "game_dvr",
            "Disable Game DVR & Background Recording",
            "Stops background Game Bar screen recording and capture hooks.",
            "GAMING", "Gaming & Input", "\uE714", "FPS STABILITY", TweakRisk.Safe, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: false, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_CURRENT_USER\System\GameConfigStore", "GameDVR_Enabled", (object)0, RegistryValueKind.DWord),
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", (object)0, RegistryValueKind.DWord)
            },
            tags: new[] { "gamedvr", "xbox", "recording" }));

        // 12. Windows Telemetry & DiagTrack (honest policy labeling)
        list.Add(BuildTelemetryTweak());

        // 13. Copilot (Win11 + Edge)
        list.Add(MakeRegistryTweak(
            "copilot_disable",
            "Disable Windows Copilot & Edge Copilot",
            "Disables Windows Copilot OS policy and Edge Copilot sidebar/hubs via official Group Policy keys.",
            "PRIVACY", "Privacy & AI", "\uE8F9", "AI CONTROL", TweakRisk.SystemWide, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: false, minBuild: 22000,
            ops: new[]
            {
                (@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", (object)1, RegistryValueKind.DWord),
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", (object)1, RegistryValueKind.DWord),
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge", "HubsSidebarEnabled", (object)0, RegistryValueKind.DWord)
            },
            tags: new[] { "copilot", "ai", "edge", "win11" }));

        // 14. Windows Recall (Win11 24H2+)
        list.Add(MakeRegistryTweak(
            "recall_disable",
            "Disable Windows Recall AI Snapshots",
            "Disables Windows Recall snapshot analysis via AllowRecallEnable / DisableAIDataAnalysis policy (24H2+).",
            "PRIVACY", "Privacy & AI", "\uE72E", "PRIVACY", TweakRisk.SystemWide, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: false, minBuild: 26100,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", (object)1, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", (object)1, RegistryValueKind.DWord)
            },
            tags: new[] { "recall", "ai", "24h2" }));

        // 15. Office 2016+/365 Telemetry
        list.Add(MakeRegistryTweak(
            "office_telemetry",
            "Disable Office 2016+ / 365 Telemetry",
            "Disables Microsoft Office OSM telemetry logging, upload, and Customer Experience Improvement Program.",
            "PRIVACY", "Privacy & AI", "\uE8A5", "OFFICE PRIVACY", TweakRisk.Safe, EvidenceRating.Measured,
            requiresAdmin: false, requiresReboot: false, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Office\16.0\osm", "Enablelogging", (object)0, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Office\16.0\osm", "EnableUpload", (object)0, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Office\16.0\common", "qmenable", (object)0, RegistryValueKind.DWord)
            },
            tags: new[] { "office", "telemetry", "privacy" }));

        // 16. Cortana & Bing Search
        list.Add(MakeRegistryTweak(
            "cortana",
            "Disable Cortana & Bing Web Search",
            "Prevents Windows Search from sending local desktop queries to Bing cloud servers.",
            "PRIVACY", "Privacy & AI", "\uE721", "LOCAL SEARCH", TweakRisk.SystemWide, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: false, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", (object)0, RegistryValueKind.DWord),
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", (object)1, RegistryValueKind.DWord),
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", (object)0, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", (object)1, RegistryValueKind.DWord)
            },
            tags: new[] { "cortana", "bing", "search" }));

        // 17. Taskbar Clutter, Widgets, Start Ads & Settings Promos
        list.Add(MakeRegistryTweak(
            "taskbar_clutter",
            "Disable Widgets, Chat, Start & Lockscreen Ads",
            "Removes taskbar Widgets/News, Meet Now/Chat, Explorer sync ads, Start recommendations, and Lockscreen tips.",
            "PRIVACY", "Privacy & AI", "\uE7B5", "CLEAN SHELL", TweakRisk.SystemWide, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: false, minBuild: 19041,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", (object)0, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", (object)0, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", (object)0, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSyncProviderNotifications", (object)0, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", (object)0, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", (object)0, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338393Enabled", (object)0, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled", (object)0, RegistryValueKind.DWord)
            },
            tags: new[] { "widgets", "ads", "start", "debloat" }));

        // 18. Privacy Dashboard: Advertising ID, Activity History, Tailored Experiences
        list.Add(MakeRegistryTweak(
            "privacy_advertising",
            "Disable Advertising ID, Activity History & Tailored Tips",
            "Turns off Windows Advertising ID, Timeline/Activity feed upload, and personalized diagnostic tips.",
            "PRIVACY", "Privacy & AI", "\uE72E", "NO AD TRACKING", TweakRisk.Safe, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: false, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", (object)0, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", (object)0, RegistryValueKind.DWord),
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", (object)0, RegistryValueKind.DWord),
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", (object)0, RegistryValueKind.DWord)
            },
            tags: new[] { "advertising", "activity", "privacy" }));

        // 19. Privacy Dashboard: Cloud Clipboard & Cross-Device Sync
        list.Add(MakeRegistryTweak(
            "privacy_clipboard",
            "Disable Cloud Clipboard Sync & Cross-Device History",
            "Prevents Windows clipboard history from syncing across devices or Microsoft cloud.",
            "PRIVACY", "Privacy & AI", "\uE77F", "CLIPBOARD", TweakRisk.SystemWide, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: false, minBuild: 17763,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard", (object)0, RegistryValueKind.DWord),
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\System", "AllowClipboardHistory", (object)0, RegistryValueKind.DWord)
            },
            tags: new[] { "clipboard", "sync", "privacy" }));

        // 20. OneDrive Policy
        list.Add(MakeRegistryTweak(
            "onedrive_disable",
            "Disable OneDrive Sync Policy",
            "Blocks OneDrive background file sync via Group Policy (your existing local OneDrive files are NOT deleted).",
            "SYSTEM", "System & I/O", "\uE753", "NO SYNC SPIKES", TweakRisk.SystemWide, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: false, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", (object)1, RegistryValueKind.DWord)
            },
            tags: new[] { "onedrive", "sync", "io" }));

        // 21. Visual Effects
        list.Add(MakeRegistryTweak(
            "visuals",
            "Optimize Visual Effects & Menu Delay",
            "Sets VisualFXSetting=2, MenuShowDelay=0, and disables transparency & minimize animations for snappier shell response.",
            "SYSTEM", "System & I/O", "\uE790", "UI SPEED", TweakRisk.Safe, EvidenceRating.Measured,
            requiresAdmin: false, requiresReboot: false, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", (object)2, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", (object)0, RegistryValueKind.DWord),
                (@"HKEY_CURRENT_USER\Control Panel\Desktop", "MenuShowDelay", (object)"0", RegistryValueKind.String),
                (@"HKEY_CURRENT_USER\Control Panel\Desktop\WindowMetrics", "MinAnimate", (object)"0", RegistryValueKind.String)
            },
            tags: new[] { "visual", "animations", "ui" }));

        // 22. Windows Search Indexer
        list.Add(BuildIndexingTweak());

        // 23. UTC Hardware Clock
        list.Add(MakeRegistryTweak(
            "utc_clock",
            "Use UTC Hardware Clock (Dual-Boot Fix)",
            "Sets RealTimeIsUniversal=1 so Windows keeps hardware RTC in UTC like Linux/macOS.",
            "SYSTEM", "System & I/O", "\uE823", "DUAL-BOOT", TweakRisk.RebootRequired, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: true, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\TimeZoneInformation", "RealTimeIsUniversal", (object)1, RegistryValueKind.DWord)
            },
            tags: new[] { "utc", "clock", "linux", "dualboot" }));

        // 24. Modern Standby (S0ix -> S3 only when S3 supported in firmware)
        list.Add(MakeRegistryTweak(
            "modern_standby",
            "Disable Modern Standby (Switch to S3 Sleep)",
            "Sets PlatformAoAcOverride=0 to prefer S3 sleep over S0 Low Power Idle. Automatically blocked if firmware lacks S3.",
            "SYSTEM", "System & I/O", "\uE708", "BATTERY / SLEEP", TweakRisk.RebootRequired, EvidenceRating.Situational,
            requiresAdmin: true, requiresReboot: true, minBuild: 19041,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power", "PlatformAoAcOverride", (object)0, RegistryValueKind.DWord)
            },
            tags: new[] { "standby", "s3", "laptop", "sleep" },
            requiresLaptop: true, requiresS3: true));

        // 25. Elite BCD Timers
        list.Add(BuildEliteBcdTimersTweak());

        // 26. Elite NTFS fsutil tweaks (via native FileSystem registry keys)
        list.Add(MakeRegistryTweak(
            "elite_fsutil",
            "Disable NTFS 8.3 Names & Last-Access Timestamps",
            "Reduces metadata write overhead on NTFS SSDs by disabling legacy 8.3 short-name generation and access-time updates.",
            "SYSTEM", "System & I/O", "\uEDA2", "SSD I/O", TweakRisk.RebootRequired, EvidenceRating.Measured,
            requiresAdmin: true, requiresReboot: true, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation", (object)1, RegistryValueKind.DWord),
                (@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", (object)unchecked((int)0x80000001), RegistryValueKind.DWord)
            },
            tags: new[] { "ntfs", "ssd", "fsutil", "elite" }));

        // 27. Memory Compression
        list.Add(MakeRegistryTweak(
            "elite_mem_compression",
            "Disable Windows Memory Compression",
            "Reduces background CPU cycles spent compressing RAM pages (best only on 32 GB+ systems).",
            "SYSTEM", "System & I/O", "\uE945", "32GB+ RAM", TweakRisk.SystemWide, EvidenceRating.Situational,
            requiresAdmin: true, requiresReboot: false, minBuild: 14393,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePageCombining", (object)1, RegistryValueKind.DWord)
            },
            tags: new[] { "ram", "compression", "elite" }));

        // 28. TCP/IP Low-Latency (Nagle + QoS reserve)
        list.Add(BuildNetTcpLowLatencyTweak());

        // 29. AMD ULPS Disable (Hardware gated)
        list.Add(MakeRegistryTweak(
            "gpu_amd_ulps",
            "Disable AMD Ultra-Low Power State (ULPS)",
            "Prevents AMD Radeon GPUs from entering deep sleep states that can cause clock-drop micro-stutters.",
            "GAMING", "Gaming & Input", "\uE7F4", "AMD CLOCKS", TweakRisk.RebootRequired, EvidenceRating.Situational,
            requiresAdmin: true, requiresReboot: true, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", "EnableUlps", (object)0, RegistryValueKind.DWord)
            },
            tags: new[] { "amd", "radeon", "ulps", "gpu" },
            gpu: GpuVendor.Amd));

        // 30. NVIDIA Display Power Saving Off (Hardware gated)
        list.Add(MakeRegistryTweak(
            "gpu_nvidia_power",
            "Disable NVIDIA Display Power Saving",
            "Disables NVIDIA driver DisplayPowerSaving backoff for consistent frame delivery.",
            "GAMING", "Gaming & Input", "\uE7F4", "NVIDIA PERF", TweakRisk.SystemWide, EvidenceRating.Situational,
            requiresAdmin: true, requiresReboot: false, minBuild: 0,
            ops: new[]
            {
                (@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NVTweak", "DisplayPowerSaving", (object)0, RegistryValueKind.DWord)
            },
            tags: new[] { "nvidia", "gpu", "power" },
            gpu: GpuVendor.Nvidia));

        return list;
    }

    private static TweakDefinition BuildPowerPlanTweak() => new()
    {
        Id = "power",
        Title = "Ultimate Performance Power Plan",
        Description = "Activates Windows highest-throughput power scheme for maximum CPU boost clocks and zero core downthrottling.",
        Category = "LATENCY",
        CategoryLabel = "Latency & CPU",
        IconGlyph = "\uE7E8",
        ImpactText = "MAX BOOST",
        Risk = TweakRisk.Safe,
        Evidence = EvidenceRating.Measured,
        RequiresAdmin = false,
        TargetDisplay = "powercfg /setactive Ultimate Performance",
        Tags = new[] { "power", "cpu", "gaming", "boost" },
        Detect = () =>
        {
            string active = GetActivePowerSchemeGuid();
            bool isUlt = active.Equals(CoreConstants.UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase);
            if (!isUlt)
            {
                var outp = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/getactivescheme" }).StdOut;
                isUlt = outp.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase);
            }
            return new TweakDetectState(isUlt, true, null, TweakBackupService.GetLatestActiveForTweak("power") != null && !isUlt);
        },
        Preview = () => new[] { new TweakChangeItem("Active Power Plan", GetActivePowerSchemeGuid(), CoreConstants.UltimatePerformanceGuid) },
        Apply = () =>
        {
            string prev = GetActivePowerSchemeGuid();
            var step = new SnapshotStep { Kind = SnapshotStepKind.PowerPlan, OriginalValueText = prev };
            var snap = new TweakSnapshot
            {
                TweakId = "power",
                Category = "LATENCY",
                Title = "Ultimate Performance Power Plan",
                KeyPath = "powercfg /getactivescheme",
                OriginalValueText = prev,
                AppliedValueDisplay = "Ultimate Performance",
                Steps = new List<SnapshotStep> { step }
            };
            var j = WriteAheadJournal.Begin("APPLY_TWEAK", "power", snap);
            try
            {
                string listOut = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/list" }).StdOut;
                string targetGuid = CoreConstants.UltimatePerformanceGuid;
                if (!listOut.Contains(targetGuid, StringComparison.OrdinalIgnoreCase))
                {
                    var lineMatch = listOut.Split('\n').FirstOrDefault(l => l.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase));
                    var m = lineMatch != null ? Regex.Match(lineMatch, @"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}") : Match.Empty;
                    if (m.Success) targetGuid = m.Value;
                    else
                    {
                        var dup = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "-duplicatescheme", CoreConstants.UltimatePerformanceGuid });
                        var dm = Regex.Match(dup.StdOut, @"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}");
                        if (dm.Success) targetGuid = dm.Value;
                    }
                }
                var setRes = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/setactive", targetGuid });
                if (setRes.ExitCode != 0)
                    throw new InvalidOperationException($"powercfg /setactive failed (exit {setRes.ExitCode}): {setRes.StdErr}".Trim());
                TweakBackupService.RecordSnapshot(snap);
                WriteAheadJournal.MarkCommitted(j.Id);
                return new TweakExecutionResult(TweakStatus.Success, "Ultimate Performance power plan activated.");
            }
            catch (Exception ex)
            {
                WriteAheadJournal.MarkFailed(j.Id, ex.Message);
                throw;
            }
        }
    };

    private static TweakDefinition BuildCoreParkingTweak() => new()
    {
        Id = "core_parking",
        Title = "Disable CPU Core Parking",
        Description = "Sets processor minimum unparked cores to 100% on AC power, reducing thread wakeup latency in heavy workloads.",
        Category = "LATENCY",
        CategoryLabel = "Latency & CPU",
        IconGlyph = "\uE950",
        ImpactText = "LOW LATENCY",
        Risk = TweakRisk.Safe,
        Evidence = EvidenceRating.Measured,
        RequiresAdmin = true,
        TargetDisplay = "powercfg SUB_PROCESSOR CPMINCORES=100",
        Tags = new[] { "cpu", "parking", "cores", "latency" },
        Detect = () =>
        {
            var res = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/query", "SCHEME_CURRENT", "SUB_PROCESSOR", "CPMINCORES" });
            // BUG FIX: Check only the AC Power Setting Index line to avoid false positives from the DC setting.
            bool unparked = Regex.IsMatch(
                res.StdOut, @"Current AC Power Setting Index:\s*0x00000064", RegexOptions.IgnoreCase);
            return new TweakDetectState(unparked, true, null, TweakBackupService.GetLatestActiveForTweak("core_parking") != null && !unparked);
        },
        Preview = () => new[] { new TweakChangeItem("SUB_PROCESSOR\\CPMINCORES (AC)", "Current", "100%") },
        Apply = () =>
        {
            string prevGuid = GetActivePowerSchemeGuid();
            var res = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/query", "SCHEME_CURRENT", "SUB_PROCESSOR", "CPMINCORES" });
            var m = Regex.Match(res.StdOut, @"Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)");
            int prevVal = m.Success ? Convert.ToInt32(m.Groups[1].Value, 16) : 10;

            // BUG FIX: Record a structured PowerSetting step so Audit Drawer, Panic Revert, and crash recovery work.
            var step = new SnapshotStep
            {
                Kind = SnapshotStepKind.PowerSetting,
                KeyPath = "SUB_PROCESSOR",
                ExtraTarget = "CPMINCORES",
                OriginalValueText = prevVal.ToString(),
                AppliedValueDisplay = "100"
            };
            var snap = new TweakSnapshot
            {
                TweakId = "core_parking",
                Category = "LATENCY",
                Title = "Disable CPU Core Parking",
                KeyPath = $"powercfg:{prevGuid}:CPMINCORES",
                OriginalValueText = prevVal.ToString(),
                AppliedValueDisplay = "100%",
                Steps = new List<SnapshotStep> { step }
            };
            var j = WriteAheadJournal.Begin("APPLY_TWEAK", "core_parking", snap);
            try
            {
                var r1 = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/setacvalueindex", "SCHEME_CURRENT", "SUB_PROCESSOR", "CPMINCORES", "100" });
                if (r1.ExitCode != 0)
                    throw new InvalidOperationException($"powercfg /setacvalueindex failed (exit {r1.ExitCode}): {r1.StdErr}".Trim());

                var r2 = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/setactive", "SCHEME_CURRENT" });
                if (r2.ExitCode != 0)
                {
                    try { TweakBackupService.RevertStep(step); } catch { }
                    throw new InvalidOperationException($"powercfg /setactive failed (exit {r2.ExitCode}): {r2.StdErr}".Trim());
                }

                TweakBackupService.RecordSnapshot(snap);
                WriteAheadJournal.MarkCommitted(j.Id);
                return new TweakExecutionResult(TweakStatus.Success, "CPU Core Parking disabled (100% min unparked cores).");
            }
            catch (Exception ex) { WriteAheadJournal.MarkFailed(j.Id, ex.Message); throw; }
        },
        Revert = () =>
        {
            var snap = TweakBackupService.GetLatestActiveForTweak("core_parking");
            if (snap == null)
                return new TweakExecutionResult(TweakStatus.Failed, "No backup snapshot recorded for Core Parking.");

            // Backwards compatibility for snapshots created before PowerSetting step existed.
            if (snap.Steps.Count == 0 && int.TryParse(snap.OriginalValueText, out int legacyOrig))
            {
                snap.Steps.Add(new SnapshotStep
                {
                    Kind = SnapshotStepKind.PowerSetting,
                    KeyPath = "SUB_PROCESSOR",
                    ExtraTarget = "CPMINCORES",
                    OriginalValueText = legacyOrig.ToString()
                });
            }

            bool ok = TweakBackupService.RevertSnapshot(snap.Id, out string msg);
            return new TweakExecutionResult(ok ? TweakStatus.Success : TweakStatus.Failed, msg);
        }
    };

    private static TweakDefinition BuildTelemetryTweak() => new()
    {
        Id = "telemetry",
        Title = "Minimize Windows Telemetry & Diagnostics",
        Description = "Sets AllowTelemetry to Security/Basic (0; Windows Home/Pro enforces Basic minimum) and disables DiagTrack/dmwappushservice.",
        Category = "PRIVACY",
        CategoryLabel = "Privacy & AI",
        IconGlyph = "\uE72E",
        ImpactText = "PRIVACY SHIELD",
        Risk = TweakRisk.SystemWide,
        Evidence = EvidenceRating.Measured,
        RequiresAdmin = true,
        TargetDisplay = "AllowTelemetry=0 | Services: DiagTrack, dmwappushservice",
        Tags = new[] { "telemetry", "diagtrack", "privacy" },
        Detect = () =>
        {
            int? val = SafeGetRegValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") as int?;
            bool applied = val == 0;
            return new TweakDetectState(applied, true, null, TweakBackupService.GetLatestActiveForTweak("telemetry") != null && !applied);
        },
        Preview = () => new[]
        {
            new TweakChangeItem(@"HKLM\...\DataCollection\AllowTelemetry", SafeGetRegValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry")?.ToString() ?? "<absent>", "0"),
            new TweakChangeItem("Service: DiagTrack", "Current", "Disabled"),
            new TweakChangeItem("Service: dmwappushservice", "Current", "Disabled")
        },
        Apply = () =>
        {
            var steps = new List<SnapshotStep>
            {
                TweakBackupService.CaptureRegistryStep(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, RegistryValueKind.DWord),
                TweakBackupService.CaptureRegistryStep(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "MaxTelemetryAllowed", 0, RegistryValueKind.DWord)
            };
            var serviceSteps = new List<SnapshotStep>();
            foreach (string svc in new[] { "DiagTrack", "dmwappushservice" })
            {
                var svcStep = TryCaptureServiceStep(svc);
                if (svcStep != null)
                {
                    steps.Add(svcStep);
                    serviceSteps.Add(svcStep);
                }
            }
            var snap = new TweakSnapshot
            {
                TweakId = "telemetry",
                Category = "PRIVACY",
                Title = "Minimize Windows Telemetry & Diagnostics",
                KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                ValueName = "AllowTelemetry",
                AppliedValueDisplay = "0 + Services Disabled",
                Steps = steps
            };
            var j = WriteAheadJournal.Begin("APPLY_TWEAK", "telemetry", snap);
            var appliedSteps = new List<SnapshotStep>();
            try
            {
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, RegistryValueKind.DWord);
                appliedSteps.Add(steps[0]);
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "MaxTelemetryAllowed", 0, RegistryValueKind.DWord);
                appliedSteps.Add(steps[1]);
                foreach (var svcStep in serviceSteps)
                {
                    string svc = svcStep.ServiceName;
                    try
                    {
                        using var sc = new ServiceController(svc);
                        if (sc.CanStop && sc.Status == ServiceControllerStatus.Running)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(4));
                        }
                        TweakBackupService.SetServiceStartMode(svc, "Disabled");
                        appliedSteps.Add(svcStep);
                    }
                    catch (InvalidOperationException ex)
                    {
                        SharedLogger.Warn("TweakCatalog", $"Telemetry service '{svc}' step note: {ex.Message}");
                    }
                }
                TweakBackupService.RecordSnapshot(snap);
                WriteAheadJournal.MarkCommitted(j.Id);
                return new TweakExecutionResult(TweakStatus.Success, "Telemetry minimized and diagnostic services disabled.");
            }
            catch (Exception ex)
            {
                for (int i = appliedSteps.Count - 1; i >= 0; i--)
                {
                    try { TweakBackupService.RevertStep(appliedSteps[i]); } catch { }
                }
                WriteAheadJournal.MarkFailed(j.Id, ex.Message);
                throw;
            }
        }
    };

    private static TweakDefinition BuildIndexingTweak() => new()
    {
        Id = "indexing",
        Title = "Disable Windows Search Indexer (WSearch)",
        Description = "Sets WSearch startup to Disabled and stops background disk indexing (note: Start/Explorer search becomes slower).",
        Category = "SYSTEM",
        CategoryLabel = "System & I/O",
        IconGlyph = "\uE8B7",
        ImpactText = "DISK I/O",
        Risk = TweakRisk.SystemWide,
        Evidence = EvidenceRating.Situational,
        RequiresAdmin = true,
        TargetDisplay = "Service: WSearch -> Disabled",
        Tags = new[] { "search", "indexing", "ssd" },
        Detect = () =>
        {
            try
            {
                using var sc = new ServiceController("WSearch");
                bool disabled = sc.StartType == ServiceStartMode.Disabled && sc.Status == ServiceControllerStatus.Stopped;
                return new TweakDetectState(disabled, true);
            }
            catch { return new TweakDetectState(false, false, "WSearch service not found."); }
        },
        Preview = () => new[] { new TweakChangeItem("Service: WSearch", "Current", "Disabled / Stopped") },
        Apply = () =>
        {
            var step = TryCaptureServiceStep("WSearch")
                ?? throw new InvalidOperationException("WSearch service was not found on this system.");
            var snap = new TweakSnapshot
            {
                TweakId = "indexing",
                Category = "SYSTEM",
                Title = "Disable Windows Search Indexer",
                KeyPath = "Service:WSearch",
                OriginalValueText = step.OriginalStartMode,
                AppliedValueDisplay = "Disabled",
                Steps = new List<SnapshotStep> { step }
            };
            var j = WriteAheadJournal.Begin("APPLY_TWEAK", "indexing", snap);
            bool stoppedService = false;
            try
            {
                using var sc = new ServiceController("WSearch");
                if (sc.CanStop && sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                    stoppedService = true;
                }
                TweakBackupService.SetServiceStartMode("WSearch", "Disabled");
                TweakBackupService.RecordSnapshot(snap);
                WriteAheadJournal.MarkCommitted(j.Id);
                return new TweakExecutionResult(TweakStatus.Success, "Windows Search Indexer disabled.");
            }
            catch (Exception ex)
            {
                if (stoppedService)
                {
                    try { TweakBackupService.RevertStep(step); } catch { }
                }
                WriteAheadJournal.MarkFailed(j.Id, ex.Message);
                throw;
            }
        }
    };

    private static TweakDefinition BuildEliteBcdTimersTweak() => new()
    {
        Id = "elite_bcd_timers",
        Title = "Disable Dynamic Tick & Force Enhanced TSC Sync",
        Description = "Configures bcdedit disabledynamictick=yes and tscsyncpolicy=Enhanced for deterministic interrupt timing (situational).",
        Category = "LATENCY",
        CategoryLabel = "Latency & CPU",
        IconGlyph = "\uE950",
        ImpactText = "TIMER SYNC",
        Risk = TweakRisk.RebootRequired,
        Evidence = EvidenceRating.Situational,
        RequiresAdmin = true,
        RequiresReboot = true,
        TargetDisplay = "bcdedit: disabledynamictick, tscsyncpolicy",
        Tags = new[] { "bcdedit", "timer", "tsc", "elite" },
        Detect = () =>
        {
            var res = NativeSystemHelper.RunSystem32ToolSync("bcdedit.exe", new[] { "/enum", "{current}" });
            // BUG FIX: "Yes" / "Enhanced" match anywhere in bcdedit output causes false positives.
            // Use line-anchored regexes to match only the relevant BCD entries.
            bool dyn = Regex.IsMatch(
                res.StdOut, @"(?m)^\s*disabledynamictick\s+Yes\s*$", RegexOptions.IgnoreCase);
            bool tsc = Regex.IsMatch(
                res.StdOut, @"(?m)^\s*tscsyncpolicy\s+Enhanced\s*$", RegexOptions.IgnoreCase);
            return new TweakDetectState(dyn && tsc, true);
        },
        Preview = () => new[]
        {
            new TweakChangeItem("bcdedit disabledynamictick", "Current", "yes"),
            new TweakChangeItem("bcdedit tscsyncpolicy", "Current", "Enhanced")
        },
        Apply = () =>
        {
            string cur = NativeSystemHelper.RunSystem32ToolSync("bcdedit.exe", new[] { "/enum", "{current}" }).StdOut;
            var dynMatch = Regex.Match(cur, @"(?im)^\s*disabledynamictick\s+(\S+)\s*$");
            var tscMatch = Regex.Match(cur, @"(?im)^\s*tscsyncpolicy\s+(\S+)\s*$");
            var dynStep = new SnapshotStep
            {
                Kind = SnapshotStepKind.BcdValue,
                ExtraTarget = "disabledynamictick",
                WasAbsent = !dynMatch.Success,
                OriginalValueText = dynMatch.Success ? dynMatch.Groups[1].Value : ""
            };
            var tscStep = new SnapshotStep
            {
                Kind = SnapshotStepKind.BcdValue,
                ExtraTarget = "tscsyncpolicy",
                WasAbsent = !tscMatch.Success,
                OriginalValueText = tscMatch.Success ? tscMatch.Groups[1].Value : ""
            };
            var steps = new List<SnapshotStep> { dynStep, tscStep };
            var snap = new TweakSnapshot
            {
                TweakId = "elite_bcd_timers",
                Category = "LATENCY",
                Title = "Disable Dynamic Tick & Force Enhanced TSC Sync",
                KeyPath = "bcdedit:{current}",
                AppliedValueDisplay = "disabledynamictick=yes, tscsyncpolicy=Enhanced",
                Steps = steps
            };
            var j = WriteAheadJournal.Begin("APPLY_TWEAK", "elite_bcd_timers", snap);
            bool firstApplied = false;
            try
            {
                var r1 = NativeSystemHelper.RunSystem32ToolSync("bcdedit.exe", new[] { "/set", "disabledynamictick", "yes" });
                if (r1.ExitCode != 0)
                    throw new InvalidOperationException($"bcdedit /set disabledynamictick failed (exit {r1.ExitCode}): {r1.StdErr}".Trim());
                firstApplied = true;

                var r2 = NativeSystemHelper.RunSystem32ToolSync("bcdedit.exe", new[] { "/set", "tscsyncpolicy", "Enhanced" });
                if (r2.ExitCode != 0)
                    throw new InvalidOperationException($"bcdedit /set tscsyncpolicy failed (exit {r2.ExitCode}): {r2.StdErr}".Trim());

                TweakBackupService.RecordSnapshot(snap);
                WriteAheadJournal.MarkCommitted(j.Id);
                return new TweakExecutionResult(TweakStatus.Success, "BCD timer policies updated.");
            }
            catch (Exception ex)
            {
                if (firstApplied)
                {
                    try { TweakBackupService.RevertStep(dynStep); } catch { }
                }
                WriteAheadJournal.MarkFailed(j.Id, ex.Message);
                throw;
            }
        }
    };

    private static TweakDefinition BuildNetTcpLowLatencyTweak() => new()
    {
        Id = "net_tcp_low_latency",
        Title = "Disable Nagle's Algorithm & QoS Bandwidth Reserve",
        Description = "Sets TcpAckFrequency=1 & TCPNoDelay=1 on active physical interfaces and removes Psched 20% NonBestEffortLimit.",
        Category = "NETWORK",
        CategoryLabel = "Network",
        IconGlyph = "\uE774",
        ImpactText = "LOW PING",
        Risk = TweakRisk.SystemWide,
        Evidence = EvidenceRating.Situational,
        RequiresAdmin = true,
        TargetDisplay = @"Tcpip\Parameters\Interfaces + Psched\NonBestEffortLimit=0",
        Tags = new[] { "nagle", "tcp", "qos", "network" },
        Detect = () =>
        {
            int? psched = SafeGetRegValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit") as int?;
            return new TweakDetectState(psched == 0, true);
        },
        Preview = () => new[]
        {
            new TweakChangeItem(@"HKLM\...\Psched\NonBestEffortLimit", SafeGetRegValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit")?.ToString() ?? "<absent>", "0"),
            new TweakChangeItem(@"Active Interfaces (TcpAckFrequency / TCPNoDelay)", "Current", "1 / 1")
        },
        Apply = () =>
        {
            var steps = new List<SnapshotStep>
            {
                TweakBackupService.CaptureRegistryStep(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit", 0, RegistryValueKind.DWord)
            };
            var activeIds = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                .Select(n => n.Id)
                .ToList();

            foreach (string guid in activeIds)
            {
                string ifPath = $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{guid}";
                steps.Add(TweakBackupService.CaptureRegistryStep(ifPath, "TcpAckFrequency", 1, RegistryValueKind.DWord));
                steps.Add(TweakBackupService.CaptureRegistryStep(ifPath, "TCPNoDelay", 1, RegistryValueKind.DWord));
            }

            var snap = new TweakSnapshot
            {
                TweakId = "net_tcp_low_latency",
                Category = "NETWORK",
                Title = "Disable Nagle's Algorithm & QoS Reserve",
                KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Psched",
                AppliedValueDisplay = $"{activeIds.Count} interface(s)",
                Steps = steps
            };

            var j = WriteAheadJournal.Begin("APPLY_TWEAK", "net_tcp_low_latency", snap);
            int appliedStepIdx = 0;
            try
            {
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit", 0, RegistryValueKind.DWord);
                appliedStepIdx++;
                foreach (string guid in activeIds)
                {
                    string ifPath = $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{guid}";
                    Registry.SetValue(ifPath, "TcpAckFrequency", 1, RegistryValueKind.DWord);
                    appliedStepIdx++;
                    Registry.SetValue(ifPath, "TCPNoDelay", 1, RegistryValueKind.DWord);
                    appliedStepIdx++;
                }
                TweakBackupService.RecordSnapshot(snap);
                WriteAheadJournal.MarkCommitted(j.Id);
                return new TweakExecutionResult(TweakStatus.Success, $"Low-latency TCP applied across {activeIds.Count} interface(s).");
            }
            catch (Exception ex)
            {
                for (int i = appliedStepIdx - 1; i >= 0; i--)
                {
                    try { TweakBackupService.RevertStep(steps[i]); } catch { }
                }
                WriteAheadJournal.MarkFailed(j.Id, ex.Message);
                throw;
            }
        }
    };
}
