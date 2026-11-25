using System;
using System.Collections.Generic;

namespace JamesOptimizer.Core;

public sealed class SmartRamSettings
{
    public bool Enabled { get; set; }
    public int FreeRamThresholdMb { get; set; } = 2048;
    public int MinStandbyMb { get; set; } = 1024;
    public int CooldownSeconds { get; set; } = 300;
    public int MaxPurgesPerHour { get; set; } = 6;
    public bool PauseDuringFullscreen { get; set; } = true;
}

public sealed class GameAutomationRule
{
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PowerPlanGuid { get; set; } = CoreConstants.UltimatePerformanceGuid;
    public string ProcessPriority { get; set; } = "High"; // Normal, AboveNormal, High
    public bool DisableEfficiencyMode { get; set; } = true;
    public bool PurgeStandbyOnLaunch { get; set; } = true;
    public bool EnableFocusAssist { get; set; } = false;
    public string CpuAffinityMaskHex { get; set; } = ""; // Optional hex mask e.g. "FF"
    public string GpuPreference { get; set; } = "HighPerformance"; // Default, HighPerformance
    public List<string> TemporarilySuspendServices { get; set; } = new();
    public bool IsEmulatorTemplate { get; set; }
}

public sealed class GameAutomationConfig
{
    public bool Enabled { get; set; }
    public List<GameAutomationRule> Rules { get; set; } = DefaultRules();

    public static List<GameAutomationRule> DefaultRules() => new()
    {
        new() { ProcessName = "cs2", DisplayName = "Counter-Strike 2" },
        new() { ProcessName = "VALORANT-Win64-Shipping", DisplayName = "Valorant" },
        new() { ProcessName = "FortniteClient-Win64-Shipping", DisplayName = "Fortnite" },
        new() { ProcessName = "HD-Player", DisplayName = "BlueStacks / MSI App Player", IsEmulatorTemplate = true },
        new() { ProcessName = "dnplayer", DisplayName = "LDPlayer Emulator", IsEmulatorTemplate = true }
    };
}
