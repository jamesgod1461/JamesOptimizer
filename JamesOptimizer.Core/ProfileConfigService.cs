using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JamesOptimizer.Core;

public enum ProfileTrustState
{
    UnverifiedExternal,
    LocalHmacVerified,
    LegacyV1
}

public sealed class ExportedOptimizerProfile
{
    public int SchemaVersion { get; set; } = 2;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
    public string AppVersion { get; set; } = CoreConstants.ProtocolVersion;
    public List<string> EnabledTweakIds { get; set; } = new();
    public GameAutomationConfig? GameAutomation { get; set; }
    public SmartRamSettings? SmartRam { get; set; }
    public string? HmacSha256 { get; set; }
    public string? Sha256Digest { get; set; }
}

public sealed record ProfileImportPreview(
    bool IsValid,
    string ErrorMessage,
    ExportedOptimizerProfile? Profile,
    IReadOnlyList<string> ToApply,
    IReadOnlyList<string> AlreadyApplied,
    IReadOnlyList<string> SkippedUnknownOrIncompatible,
    ProfileTrustState TrustState = ProfileTrustState.UnverifiedExternal,
    bool RequiresConfirmation = true,
    string? LegacyUnauthenticatedWarning = null);

[SupportedOSPlatform("windows")]
public static class ProfileConfigService
{
    private static readonly object _keyLock = new();
    private static readonly byte[] KeyEntropy = "JamesOptimizer.ProfileHmac.v2"u8.ToArray();

    private static string _hmacKeyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CoreConstants.ProductName, "profile_hmac.bin");

    public static void ConfigureKeyPath(string keyFilePath)
    {
        if (!string.IsNullOrWhiteSpace(keyFilePath))
            _hmacKeyPath = keyFilePath;
    }

    public static ExportedOptimizerProfile ExportCurrentProfile(GameAutomationConfig? gameCfg = null, SmartRamSettings? ramCfg = null)
    {
        var enabledIds = new List<string>();
        foreach (var tweak in TweakCatalog.All)
        {
            try
            {
                var st = tweak.Detect();
                if (st.IsApplicable && st.IsApplied)
                    enabledIds.Add(tweak.Id);
            }
            catch { }
        }

        var profile = new ExportedOptimizerProfile
        {
            SchemaVersion = 2,
            ExportedAt = DateTimeOffset.Now,
            AppVersion = CoreConstants.ProtocolVersion,
            EnabledTweakIds = enabledIds,
            GameAutomation = gameCfg,
            SmartRam = ramCfg
        };
        SignProfile(profile);
        return profile;
    }

    public static void SaveProfileToFile(string path, ExportedOptimizerProfile profile)
    {
        SignProfile(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    public static ProfileImportPreview PreviewImport(string jsonContent)
    {
        ExportedOptimizerProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<ExportedOptimizerProfile>(jsonContent);
        }
        catch (Exception ex)
        {
            return InvalidPreview($"Invalid profile JSON: {ex.Message}");
        }

        if (profile == null || profile.SchemaVersion < 1 || profile.SchemaVersion > 2 || profile.EnabledTweakIds == null)
            return InvalidPreview("Malformed or unsupported profile schema.");

        ProfileTrustState trustState;
        bool requiresConfirmation;
        string? warning = null;

        if (profile.SchemaVersion == 1)
        {
            if (!string.IsNullOrWhiteSpace(profile.Sha256Digest))
            {
                string expected = ComputeLegacyDigest(profile);
                if (!expected.Equals(profile.Sha256Digest, StringComparison.OrdinalIgnoreCase))
                    return InvalidPreview("Legacy v1 profile integrity hash mismatch (file was modified after export).");
            }

            trustState = ProfileTrustState.LegacyV1;
            requiresConfirmation = true;
            warning = "Legacy v1 profile lacks cryptographic HMAC authentication. Review tweaks and confirm before applying.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(profile.HmacSha256))
            {
                trustState = ProfileTrustState.UnverifiedExternal;
                requiresConfirmation = true;
                warning = "v2 profile is missing HMAC integrity tag. Treat as unverified external profile.";
            }
            else
            {
                byte[]? key = TryReadExistingHmacKey();
                if (key == null)
                {
                    trustState = ProfileTrustState.UnverifiedExternal;
                    requiresConfirmation = true;
                    warning = "Local HMAC verification key is missing or unreadable. Profile cannot be verified on this user/machine.";
                }
                else
                {
                    bool verified = false;
                    try
                    {
                        byte[] provided = Convert.FromHexString(profile.HmacSha256.Trim());
                        if (provided.Length == 32)
                        {
                            byte[] expected = HMACSHA256.HashData(key, ComputeCanonicalPayloadBytes(profile));
                            verified = CryptographicOperations.FixedTimeEquals(expected, provided);
                        }
                    }
                    catch (FormatException)
                    {
                        verified = false;
                    }

                    if (verified)
                    {
                        trustState = ProfileTrustState.LocalHmacVerified;
                        requiresConfirmation = false;
                    }
                    else
                    {
                        trustState = ProfileTrustState.UnverifiedExternal;
                        requiresConfirmation = true;
                        warning = "Profile HMAC verification failed (file may be modified or exported on another account/PC).";
                    }
                }
            }
        }

        // Always sanitize catalog tweaks, GameAutomation rules, and SmartRam settings regardless of trust state.
        SanitizeGameAndRamSettings(profile);

        var toApply = new List<string>();
        var alreadyApplied = new List<string>();
        var skipped = new List<string>();

        foreach (string rawId in profile.EnabledTweakIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var def = TweakCatalog.Find(rawId);
            if (def == null)
            {
                skipped.Add($"{rawId} (unknown catalog id)");
                continue;
            }

            var st = def.Detect();
            if (!st.IsApplicable)
                skipped.Add($"{def.Id} ({st.NotApplicableReason ?? "not applicable"})");
            else if (st.IsApplied)
                alreadyApplied.Add(def.Id);
            else
                toApply.Add(def.Id);
        }

        return new ProfileImportPreview(
            true,
            "",
            profile,
            toApply,
            alreadyApplied,
            skipped,
            trustState,
            requiresConfirmation,
            warning);
    }

    public static (int Applied, int Failed, List<string> Messages) ApplyPreviewedProfile(ProfileImportPreview preview, bool userConfirmed = false)
    {
        if (!preview.IsValid || preview.Profile == null)
            return (0, 1, new List<string> { preview.ErrorMessage });

        if (preview.RequiresConfirmation && !userConfirmed)
            return (0, 1, new List<string> { "Explicit confirmation required before applying an unverified or legacy v1 profile." });

        int applied = 0, failed = 0;
        var messages = new List<string>();

        foreach (string id in preview.ToApply)
        {
            var res = TweakCatalog.ApplyTweak(id);
            if (res.Status is TweakStatus.Success or TweakStatus.NeedsReboot or TweakStatus.AlreadyApplied)
            {
                applied++;
                messages.Add($"[OK] {id}: {res.Message}");
            }
            else
            {
                failed++;
                messages.Add($"[FAIL] {id}: {res.Message}");
            }
        }

        return (applied, failed, messages);
    }

    /// <summary>
    /// Computes deterministic UTF-8 canonical bytes covering the entire functional payload
    /// (SchemaVersion, AppVersion, ExportedAt, sorted EnabledTweakIds, GameAutomation, SmartRam).
    /// </summary>
    public static byte[] ComputeCanonicalPayloadBytes(ExportedOptimizerProfile profile)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", profile.SchemaVersion);
            w.WriteString("appVersion", profile.AppVersion ?? "");
            w.WriteString("exportedAtUtc", profile.ExportedAt.ToUniversalTime().ToString("O"));

            w.WritePropertyName("enabledTweakIds");
            w.WriteStartArray();
            if (profile.EnabledTweakIds != null)
            {
                foreach (string id in profile.EnabledTweakIds
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    w.WriteStringValue(id);
                }
            }
            w.WriteEndArray();

            w.WritePropertyName("gameAutomation");
            if (profile.GameAutomation == null)
            {
                w.WriteNullValue();
            }
            else
            {
                w.WriteStartObject();
                w.WriteBoolean("enabled", profile.GameAutomation.Enabled);
                w.WritePropertyName("rules");
                w.WriteStartArray();
                var rules = profile.GameAutomation.Rules ?? new List<GameAutomationRule>();
                foreach (var r in rules
                    .OrderBy(x => x.ProcessName ?? "", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.DisplayName ?? "", StringComparer.OrdinalIgnoreCase))
                {
                    w.WriteStartObject();
                    w.WriteString("processName", r.ProcessName ?? "");
                    w.WriteString("displayName", r.DisplayName ?? "");
                    w.WriteString("powerPlanGuid", r.PowerPlanGuid ?? "");
                    w.WriteString("processPriority", r.ProcessPriority ?? "");
                    w.WriteBoolean("disableEfficiencyMode", r.DisableEfficiencyMode);
                    w.WriteBoolean("purgeStandbyOnLaunch", r.PurgeStandbyOnLaunch);
                    w.WriteBoolean("enableFocusAssist", r.EnableFocusAssist);
                    w.WriteString("cpuAffinityMaskHex", r.CpuAffinityMaskHex ?? "");
                    w.WriteString("gpuPreference", r.GpuPreference ?? "");
                    w.WriteBoolean("isEmulatorTemplate", r.IsEmulatorTemplate);
                    w.WritePropertyName("temporarilySuspendServices");
                    w.WriteStartArray();
                    if (r.TemporarilySuspendServices != null)
                    {
                        foreach (string svc in r.TemporarilySuspendServices
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                        {
                            w.WriteStringValue(svc);
                        }
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WritePropertyName("smartRam");
            if (profile.SmartRam == null)
            {
                w.WriteNullValue();
            }
            else
            {
                w.WriteStartObject();
                w.WriteBoolean("enabled", profile.SmartRam.Enabled);
                w.WriteNumber("freeRamThresholdMb", profile.SmartRam.FreeRamThresholdMb);
                w.WriteNumber("minStandbyMb", profile.SmartRam.MinStandbyMb);
                w.WriteNumber("cooldownSeconds", profile.SmartRam.CooldownSeconds);
                w.WriteNumber("maxPurgesPerHour", profile.SmartRam.MaxPurgesPerHour);
                w.WriteBoolean("pauseDuringFullscreen", profile.SmartRam.PauseDuringFullscreen);
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private static void SignProfile(ExportedOptimizerProfile profile)
    {
        if (profile.SchemaVersion <= 1)
        {
            profile.HmacSha256 = null;
            profile.Sha256Digest = ComputeLegacyDigest(profile);
            return;
        }

        profile.Sha256Digest = null;
        byte[] key = GetOrCreateHmacKey();
        byte[] mac = HMACSHA256.HashData(key, ComputeCanonicalPayloadBytes(profile));
        profile.HmacSha256 = Convert.ToHexString(mac);
    }

    private static void SanitizeGameAndRamSettings(ExportedOptimizerProfile profile)
    {
        if (profile.GameAutomation != null)
        {
            profile.GameAutomation.Rules ??= new List<GameAutomationRule>();
            var sanitizedRules = new List<GameAutomationRule>();
            foreach (var r in profile.GameAutomation.Rules)
            {
                if (r == null) continue;
                string rawProc = (r.ProcessName ?? "").Replace(';', ' ').Replace(',', ' ').Trim();
                if (!IpcProtocol.TrySanitizeGameList(rawProc, out string cleanProc) ||
                    string.IsNullOrWhiteSpace(cleanProc) ||
                    cleanProc.Contains(';'))
                {
                    continue;
                }

                r.ProcessName = cleanProc;
                r.TemporarilySuspendServices ??= new List<string>();
                r.TemporarilySuspendServices.RemoveAll(CoreConstants.IsProtectedService);

                if (!string.Equals(r.ProcessPriority, "Normal", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(r.ProcessPriority, "AboveNormal", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(r.ProcessPriority, "High", StringComparison.OrdinalIgnoreCase))
                {
                    r.ProcessPriority = "High";
                }

                if (!Guid.TryParse(r.PowerPlanGuid, out _))
                    r.PowerPlanGuid = CoreConstants.UltimatePerformanceGuid;

                if (!string.Equals(r.GpuPreference, "Default", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(r.GpuPreference, "HighPerformance", StringComparison.OrdinalIgnoreCase))
                {
                    r.GpuPreference = "HighPerformance";
                }

                sanitizedRules.Add(r);
            }
            profile.GameAutomation.Rules = sanitizedRules;
        }

        if (profile.SmartRam != null)
        {
            profile.SmartRam.FreeRamThresholdMb = Math.Clamp(profile.SmartRam.FreeRamThresholdMb, 256, 65536);
            profile.SmartRam.MinStandbyMb = Math.Clamp(profile.SmartRam.MinStandbyMb, 128, 65536);
            profile.SmartRam.CooldownSeconds = Math.Clamp(profile.SmartRam.CooldownSeconds, 30, 86400);
            profile.SmartRam.MaxPurgesPerHour = Math.Clamp(profile.SmartRam.MaxPurgesPerHour, 1, 60);
        }
    }

    private static byte[] GetOrCreateHmacKey()
    {
        lock (_keyLock)
        {
            byte[]? existing = TryReadExistingHmacKeyUnlocked();
            if (existing != null) return existing;

            byte[] key = RandomNumberGenerator.GetBytes(32);
            byte[] protectedBytes = ProtectedData.Protect(key, KeyEntropy, DataProtectionScope.CurrentUser);
            string fullPath = Path.GetFullPath(_hmacKeyPath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = $"{fullPath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(tmp, protectedBytes);
            File.Move(tmp, fullPath, overwrite: true);
            return key;
        }
    }

    private static byte[]? TryReadExistingHmacKey()
    {
        lock (_keyLock)
            return TryReadExistingHmacKeyUnlocked();
    }

    private static byte[]? TryReadExistingHmacKeyUnlocked()
    {
        try
        {
            if (!File.Exists(_hmacKeyPath)) return null;
            byte[] encrypted = File.ReadAllBytes(_hmacKeyPath);
            if (encrypted.Length == 0) return null;
            byte[] key = ProtectedData.Unprotect(encrypted, KeyEntropy, DataProtectionScope.CurrentUser);
            return key.Length == 32 ? key : null;
        }
        catch
        {
            return null;
        }
    }

    private static ProfileImportPreview InvalidPreview(string error) =>
        new(false, error, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    private static string ComputeLegacyDigest(ExportedOptimizerProfile profile)
    {
        string canonical = $"{profile.SchemaVersion}|{profile.AppVersion}|{string.Join(',', profile.EnabledTweakIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }
}
