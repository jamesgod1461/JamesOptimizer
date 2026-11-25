using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JamesOptimizer.Core;

public sealed class ExportedOptimizerProfile
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
    public string AppVersion { get; set; } = CoreConstants.ProtocolVersion;
    public List<string> EnabledTweakIds { get; set; } = new();
    public GameAutomationConfig? GameAutomation { get; set; }
    public SmartRamSettings? SmartRam { get; set; }
    public string? Sha256Digest { get; set; }
}

public sealed record ProfileImportPreview(
    bool IsValid,
    string ErrorMessage,
    ExportedOptimizerProfile? Profile,
    IReadOnlyList<string> ToApply,
    IReadOnlyList<string> AlreadyApplied,
    IReadOnlyList<string> SkippedUnknownOrIncompatible);

[SupportedOSPlatform("windows")]
public static class ProfileConfigService
{
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
            SchemaVersion = 1,
            ExportedAt = DateTimeOffset.Now,
            AppVersion = CoreConstants.ProtocolVersion,
            EnabledTweakIds = enabledIds,
            GameAutomation = gameCfg,
            SmartRam = ramCfg
        };
        profile.Sha256Digest = ComputeCanonicalDigest(profile);
        return profile;
    }

    public static void SaveProfileToFile(string path, ExportedOptimizerProfile profile)
    {
        profile.Sha256Digest = ComputeCanonicalDigest(profile);
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
            return new ProfileImportPreview(false, $"Invalid profile JSON: {ex.Message}", null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        }

        if (profile == null || profile.SchemaVersion < 1 || profile.EnabledTweakIds == null)
            return new ProfileImportPreview(false, "Malformed or unsupported profile schema.", null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        if (!string.IsNullOrWhiteSpace(profile.Sha256Digest))
        {
            string expected = ComputeCanonicalDigest(profile);
            if (!expected.Equals(profile.Sha256Digest, StringComparison.OrdinalIgnoreCase))
                return new ProfileImportPreview(false, "Profile integrity hash mismatch (file was modified after export).", null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        }

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

        return new ProfileImportPreview(true, "", profile, toApply, alreadyApplied, skipped);
    }

    public static (int Applied, int Failed, List<string> Messages) ApplyPreviewedProfile(ProfileImportPreview preview)
    {
        if (!preview.IsValid || preview.Profile == null)
            return (0, 1, new List<string> { preview.ErrorMessage });

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

    private static string ComputeCanonicalDigest(ExportedOptimizerProfile profile)
    {
        string canonical = $"{profile.SchemaVersion}|{profile.AppVersion}|{string.Join(',', profile.EnabledTweakIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }
}
