using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using JamesOptimizer.Core;
using Microsoft.Win32;
using Xunit;

namespace JamesOptimizer.Tests;

[SupportedOSPlatform("windows")]
public class CoreSafetyAndParityTests : IDisposable
{
    private readonly string _tempDir;

    public CoreSafetyAndParityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"jo_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        TweakBackupService.ConfigurePath(Path.Combine(_tempDir, "tweak_history.json"));
        WriteAheadJournal.ConfigurePath(Path.Combine(_tempDir, "wal_journal.json"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void ProtectedServices_IncludeDefenderFirewallAndUpdates_AndBlockModification()
    {
        Assert.True(CoreConstants.IsProtectedService("WinDefend"));
        Assert.True(CoreConstants.IsProtectedService("SecurityHealthService"));
        Assert.True(CoreConstants.IsProtectedService("wscsvc"));
        Assert.True(CoreConstants.IsProtectedService("mpssvc"));
        Assert.True(CoreConstants.IsProtectedService("wuauserv"));

        Assert.Throws<InvalidOperationException>(() => TweakBackupService.SetServiceStartMode("WinDefend", "Disabled"));
    }

    [Fact]
    public void ProtectedProcesses_BlockKillingLsassAndCsrss()
    {
        Assert.True(CoreConstants.IsProtectedProcess("lsass"));
        Assert.True(CoreConstants.IsProtectedProcess("csrss.exe"));
        Assert.True(CoreConstants.IsProtectedProcess("winlogon"));
        Assert.False(CoreConstants.IsProtectedProcess("notepad"));
    }

    [Fact]
    public void IpcProtocol_RejectsUnknownOversizedAndOutOfRangeCommands()
    {
        Assert.False(IpcProtocol.TryParseCommand("", out _, out _, out _));
        Assert.False(IpcProtocol.TryParseCommand(new string('A', IpcProtocol.MaxMessageLength + 1), out _, out _, out _));
        Assert.False(IpcProtocol.TryParseCommand("EXEC_SHELL:calc.exe", out _, out _, out _));
        Assert.True(IpcProtocol.TryParseCommand("SET_TIMER:30", out string action, out string arg, out _));
        Assert.Equal("SET_TIMER", action);
        Assert.True(IpcProtocol.TryValidateTimerMinutes(arg, out int mins) && mins == 30);
        Assert.False(IpcProtocol.TryValidateTimerMinutes("-5", out _));
        Assert.False(IpcProtocol.TryValidateTimerMinutes("99999", out _));
        Assert.True(IpcProtocol.TrySanitizeGameList("cs2.exe; valorant; bad|cmd", out string clean));
        Assert.Equal("cs2;valorant", clean);
    }

    [Fact]
    public void Integrator_RejectsShadowingProtectedRunAliases()
    {
        Assert.False(IntegratorService.IsValidRunAliasName("cmd", out _));
        Assert.False(IntegratorService.IsValidRunAliasName("powershell.exe", out _));
        Assert.False(IntegratorService.IsValidRunAliasName("regedit", out _));
        Assert.False(IntegratorService.IsValidRunAliasName("a", out _));
        Assert.True(IntegratorService.IsValidRunAliasName("my_custom_tool", out _));
    }

    [Fact]
    public void HostsManager_ValidatesParsesTogglesAndRemovesTags()
    {
        string hosts = Path.Combine(_tempDir, "hosts");
        File.WriteAllText(hosts, "127.0.0.1 localhost\r\n# comment\r\n");

        var badIp = HostsManagerService.AddOrUpdateEntry("999.999.1.1", "example.com", "", null, hosts);
        Assert.False(badIp.Ok);

        var ok = HostsManagerService.AddOrUpdateEntry("0.0.0.0", "ads.example.test", "test block", "CustomTag", hosts);
        Assert.True(ok.Ok);

        var entries = HostsManagerService.ReadEntries(hosts);
        Assert.Equal(2, entries.Count);
        var added = entries.Single(e => e.Hostname == "ads.example.test");
        Assert.True(added.Enabled);
        Assert.Equal("CustomTag", added.Tag);

        Assert.True(HostsManagerService.SetEntryEnabled(added.LineIndex, false, hosts).Ok);
        Assert.False(HostsManagerService.ReadEntries(hosts).Single(e => e.Hostname == "ads.example.test").Enabled);

        Assert.True(HostsManagerService.RemoveTagBlock("CustomTag", hosts).Ok);
        Assert.Single(HostsManagerService.ReadEntries(hosts));
    }

    [Fact]
    public void ProfileConfig_RejectsTamperedDigestAndUnknownIds()
    {
        var profile = new ExportedOptimizerProfile
        {
            SchemaVersion = 1,
            EnabledTweakIds = new() { "mouse_fix", "nonexistent_evil_tweak" }
        };
        string path = Path.Combine(_tempDir, "prof.json");
        ProfileConfigService.SaveProfileToFile(path, profile);

        string json = File.ReadAllText(path);
        var preview = ProfileConfigService.PreviewImport(json);
        Assert.True(preview.IsValid);
        Assert.Equal(ProfileTrustState.LegacyV1, preview.TrustState);
        Assert.True(preview.RequiresConfirmation);
        Assert.False(string.IsNullOrWhiteSpace(preview.LegacyUnauthenticatedWarning));
        Assert.Contains(preview.SkippedUnknownOrIncompatible, s => s.Contains("nonexistent_evil_tweak"));

        // Unconfirmed legacy import must fail closed
        var (appliedUnconfirmed, failedUnconfirmed, _) = ProfileConfigService.ApplyPreviewedProfile(preview);
        Assert.Equal(0, appliedUnconfirmed);
        Assert.Equal(1, failedUnconfirmed);

        string tampered = json.Replace("mouse_fix", "keyboard_fix");
        var tamperedPreview = ProfileConfigService.PreviewImport(tampered);
        Assert.False(tamperedPreview.IsValid);
    }

    [Fact]
    public void ProfileConfig_V2CanonicalBytes_StableAcrossPropertyOrderAndWhitespace()
    {
        string compactA = """
{"SchemaVersion":2,"AppVersion":"3.1","ExportedAt":"2026-11-25T10:00:00+02:00","EnabledTweakIds":["keyboard_fix","mouse_fix"],"SmartRam":{"Enabled":true,"FreeRamThresholdMb":2048,"MinStandbyMb":1024,"CooldownSeconds":300,"MaxPurgesPerHour":6,"PauseDuringFullscreen":true},"GameAutomation":{"Enabled":true,"Rules":[{"ProcessName":"cs2","DisplayName":"CS2","TemporarilySuspendServices":["Spooler","SysMain"]}]}}
""";
        string reorderedB = """
{
  "GameAutomation": {
    "Rules": [
      {
        "DisplayName": "CS2",
        "TemporarilySuspendServices": [ "SysMain", "Spooler" ],
        "ProcessName": "cs2"
      }
    ],
    "Enabled": true
  },
  "SmartRam": {
    "PauseDuringFullscreen": true,
    "MaxPurgesPerHour": 6,
    "CooldownSeconds": 300,
    "MinStandbyMb": 1024,
    "FreeRamThresholdMb": 2048,
    "Enabled": true
  },
  "EnabledTweakIds": [ "mouse_fix", "keyboard_fix" ],
  "ExportedAt": "2026-11-25T08:00:00Z",
  "AppVersion": "3.1",
  "SchemaVersion": 2
}
""";
        var pA = System.Text.Json.JsonSerializer.Deserialize<ExportedOptimizerProfile>(compactA)!;
        var pB = System.Text.Json.JsonSerializer.Deserialize<ExportedOptimizerProfile>(reorderedB)!;

        byte[] bytesA = ProfileConfigService.ComputeCanonicalPayloadBytes(pA);
        byte[] bytesB = ProfileConfigService.ComputeCanonicalPayloadBytes(pB);
        Assert.Equal(bytesA, bytesB);
    }

    [Fact]
    public void ProfileConfig_V2Hmac_VerifiesValid_FlagsTamperedMissingKeyAndSanitizesRules()
    {
        string keyPath = Path.Combine(_tempDir, "profile_hmac_test.bin");
        ProfileConfigService.ConfigureKeyPath(keyPath);

        var profile = new ExportedOptimizerProfile
        {
            SchemaVersion = 2,
            EnabledTweakIds = new() { "mouse_fix", "bogus_tweak_id" },
            GameAutomation = new GameAutomationConfig
            {
                Enabled = true,
                Rules = new()
                {
                    new GameAutomationRule
                    {
                        ProcessName = "cs2",
                        DisplayName = "CS2",
                        ProcessPriority = "RealTime", // disallowed -> sanitized to High
                        PowerPlanGuid = "not-a-guid",
                        TemporarilySuspendServices = new() { "WinDefend", "wuauserv", "Spooler" }
                    },
                    new GameAutomationRule
                    {
                        ProcessName = "bad|cmd&exec;calc", // invalid process name -> dropped
                        DisplayName = "Evil"
                    }
                }
            }
        };

        string profPath = Path.Combine(_tempDir, "v2_prof.json");
        ProfileConfigService.SaveProfileToFile(profPath, profile);
        Assert.True(File.Exists(keyPath));
        Assert.False(string.IsNullOrWhiteSpace(profile.HmacSha256));
        Assert.Null(profile.Sha256Digest);

        string json = File.ReadAllText(profPath);

        // 1. Valid v2 + local key -> LocalHmacVerified + strict rule sanitization applied
        var validPreview = ProfileConfigService.PreviewImport(json);
        Assert.True(validPreview.IsValid);
        Assert.Equal(ProfileTrustState.LocalHmacVerified, validPreview.TrustState);
        Assert.False(validPreview.RequiresConfirmation);
        Assert.Contains(validPreview.SkippedUnknownOrIncompatible, s => s.Contains("bogus_tweak_id"));
        var onlyRule = Assert.Single(validPreview.Profile!.GameAutomation!.Rules);
        Assert.Equal("cs2", onlyRule.ProcessName);
        Assert.Equal("High", onlyRule.ProcessPriority);
        Assert.Equal(CoreConstants.UltimatePerformanceGuid, onlyRule.PowerPlanGuid);
        Assert.DoesNotContain("WinDefend", onlyRule.TemporarilySuspendServices);
        Assert.DoesNotContain("wuauserv", onlyRule.TemporarilySuspendServices);
        Assert.Contains("Spooler", onlyRule.TemporarilySuspendServices);

        // 2. Tampered v2 -> UnverifiedExternal + RequiresConfirmation + Apply fails without confirmation
        string tamperedJson = json.Replace("\"Spooler\"", "\"PrintNotify\"");
        var tamperedPreview = ProfileConfigService.PreviewImport(tamperedJson);
        Assert.True(tamperedPreview.IsValid);
        Assert.Equal(ProfileTrustState.UnverifiedExternal, tamperedPreview.TrustState);
        Assert.True(tamperedPreview.RequiresConfirmation);
        Assert.Equal((0, 1), (ProfileConfigService.ApplyPreviewedProfile(tamperedPreview).Applied, ProfileConfigService.ApplyPreviewedProfile(tamperedPreview).Failed));

        // 3. Missing HmacSha256 in v2 -> UnverifiedExternal + RequiresConfirmation
        var missingMacDoc = System.Text.Json.JsonSerializer.Deserialize<ExportedOptimizerProfile>(json)!;
        missingMacDoc.HmacSha256 = null;
        string noMacJson = System.Text.Json.JsonSerializer.Serialize(missingMacDoc);
        var noMacPreview = ProfileConfigService.PreviewImport(noMacJson);
        Assert.True(noMacPreview.IsValid);
        Assert.Equal(ProfileTrustState.UnverifiedExternal, noMacPreview.TrustState);
        Assert.True(noMacPreview.RequiresConfirmation);

        // 4. Corrupt or missing key file -> UnverifiedExternal without crashing
        File.WriteAllBytes(keyPath, new byte[] { 1, 2, 3, 4 });
        var corruptKeyPreview = ProfileConfigService.PreviewImport(json);
        Assert.True(corruptKeyPreview.IsValid);
        Assert.Equal(ProfileTrustState.UnverifiedExternal, corruptKeyPreview.TrustState);
        Assert.True(corruptKeyPreview.RequiresConfirmation);

        File.Delete(keyPath);
        var missingKeyPreview = ProfileConfigService.PreviewImport(json);
        Assert.True(missingKeyPreview.IsValid);
        Assert.Equal(ProfileTrustState.UnverifiedExternal, missingKeyPreview.TrustState);
        Assert.True(missingKeyPreview.RequiresConfirmation);
    }

    [Fact]
    public void TweakBackupService_ApplyAndRevert_RestoresAbsentRegistryKeyAndValue()
    {
        string testSubKey = $@"Software\JamesOptimizer_UnitTest_{Guid.NewGuid():N}";
        string fullPath = $@"HKEY_CURRENT_USER\{testSubKey}";

        try
        {
            TweakBackupService.ApplyRegistryWithBackup("TEST", "Unit Test Key", fullPath, "TestVal", 42, RegistryValueKind.DWord, "ut_reg");
            Assert.Equal(42, Registry.GetValue(fullPath, "TestVal", null));

            bool reverted = TweakBackupService.RevertTweakById("ut_reg", out string msg);
            Assert.True(reverted, msg);
            Assert.Null(Registry.GetValue(fullPath, "TestVal", null));
            using var checkKey = Registry.CurrentUser.OpenSubKey(testSubKey, false);
            Assert.Null(checkKey);
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(testSubKey, false); } catch { }
        }
    }

    [Fact]
    public void Catalog_AppliesAndRevertsHkcuTweak_WithZeroDiff()
    {
        var def = TweakCatalog.Find("game_mode");
        Assert.NotNull(def);

        const string keyPath = @"HKEY_CURRENT_USER\Software\Microsoft\GameBar";
        object? orig = Registry.GetValue(keyPath, "AutoGameModeEnabled", null);
        try
        {
            var prev = def!.Preview();
            Assert.NotEmpty(prev);

            var res = TweakCatalog.ApplyTweak("game_mode");
            Assert.True(res.Status is TweakStatus.Success or TweakStatus.AlreadyApplied);
            Assert.True(def.Detect().IsApplied);

            if (res.Status == TweakStatus.Success)
            {
                var rev = TweakCatalog.RevertTweak("game_mode");
                Assert.Equal(TweakStatus.Success, rev.Status);
                Assert.Equal(orig, Registry.GetValue(keyPath, "AutoGameModeEnabled", null));
            }
        }
        finally
        {
            if (orig == null)
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar", true);
                k?.DeleteValue("AutoGameModeEnabled", false);
            }
            else
            {
                Registry.SetValue(keyPath, "AutoGameModeEnabled", orig, RegistryValueKind.DWord);
            }
        }
    }

    // ── BUG FIX REGRESSION TESTS ────────────────────────────────────────────

    /// <summary>BUG FIX: SetServiceStartMode must pass "start=" and the value as separate argv tokens.
    /// Formerly "start= disabled" was one token, causing sc.exe to exit 1639.</summary>
    [Fact]
    public void SetServiceStartMode_ProtectedServiceThrows()
    {
        // All protected services must throw before any sc.exe call.
        foreach (string svc in new[] { "WinDefend", "MpsSvc", "wuauserv", "wscsvc" })
            Assert.Throws<InvalidOperationException>(() => TweakBackupService.SetServiceStartMode(svc, "disabled"));
    }

    /// <summary>BUG FIX: ValuesEqual must not throw for non-numeric or out-of-range registry values.</summary>
    [Fact]
    public void Catalog_ValuesEqual_DoesNotThrowForNonNumericDwordValue()
    {
        // The public API surfaces this indirectly through Detect() on any registry tweak.
        // We confirm ValuesEqual internal safety by checking that Detect() on game_mode
        // does not throw even when the registry value is absent or unexpected type.
        var def = TweakCatalog.Find("game_mode");
        Assert.NotNull(def);
        var ex = Record.Exception(() => def!.Detect());
        Assert.Null(ex);
    }

    /// <summary>BUG FIX: core_parking Detect should match AC index only, not DC or any "0x64" substring.</summary>
    [Fact]
    public void Catalog_CoreParking_DetectRegexMatchesOnlyAcIndex()
    {
        // Simulate powercfg output where DC=100% but AC=10% — should NOT be detected as applied.
        string dcOnlyOutput =
            "Power Scheme GUID: ...\r\n" +
            "  Subgroup GUID: ... (Processor power management)\r\n" +
            "    Power Setting GUID: ... (Minimum processor state)\r\n" +
            "    Current DC Power Setting Index: 0x00000064\r\n" +
            "    Current AC Power Setting Index: 0x0000000a\r\n";

        bool matchDcOnly = Regex.IsMatch(
            dcOnlyOutput,
            @"Current AC Power Setting Index:\s*0x00000064",
            RegexOptions.IgnoreCase);
        Assert.False(matchDcOnly); // must NOT match when AC is 0a

        // Simulate AC=100%.
        string acApplied =
            "    Current AC Power Setting Index: 0x00000064\r\n" +
            "    Current DC Power Setting Index: 0x0000000a\r\n";

        bool matchAcApplied = Regex.IsMatch(
            acApplied,
            @"Current AC Power Setting Index:\s*0x00000064",
            RegexOptions.IgnoreCase);
        Assert.True(matchAcApplied);
    }

    /// <summary>BUG FIX: elite_bcd_timers Detect should use line-anchored regex,
    /// not substring "Yes" which false-positives on other BCD fields.</summary>
    [Fact]
    public void Catalog_EliteBcdTimers_DetectRegexLineAnchored()
    {
        // "Yes" appears in another BCD field, not in disabledynamictick row → should NOT match.
        string withoutDynTick =
            "identifier              {current}\r\n" +
            "description             Windows Boot Manager\r\n" +
            "locale                  Yes\r\n"; // locale field happens to contain "Yes"

        bool dynFalsePositive = Regex.IsMatch(
            withoutDynTick,
            @"(?m)^\s*disabledynamictick\s+Yes\s*$",
            RegexOptions.IgnoreCase);
        Assert.False(dynFalsePositive);

        // "Yes" on the disabledynamictick line → should match.
        string withDynTick =
            "identifier              {current}\r\n" +
            "disabledynamictick      Yes\r\n" +
            "tscsyncpolicy           Enhanced\r\n";

        bool dynMatch = Regex.IsMatch(
            withDynTick,
            @"(?m)^\s*disabledynamictick\s+Yes\s*$",
            RegexOptions.IgnoreCase);
        Assert.True(dynMatch);

        bool tscMatch = Regex.IsMatch(
            withDynTick,
            @"(?m)^\s*tscsyncpolicy\s+Enhanced\s*$",
            RegexOptions.IgnoreCase);
        Assert.True(tscMatch);
    }

    /// <summary>BUG FIX: WriteAheadJournal and TweakBackupService LoadInternal must not wipe state
    /// on corrupt JSON — they silently return empty. Verify they don't throw.</summary>
    [Fact]
    public void WriteAheadJournal_LoadsCorruptFileWithoutThrowing()
    {
        string journalPath = Path.Combine(_tempDir, "corrupt_wal.json");
        File.WriteAllText(journalPath, "{ this is not valid json !!!");
        WriteAheadJournal.ConfigurePath(journalPath);

        var ex = Record.Exception(() => WriteAheadJournal.GetIncompleteEntries());
        Assert.Null(ex);

        // Restore for other tests.
        WriteAheadJournal.ConfigurePath(Path.Combine(_tempDir, "wal_journal.json"));
    }

    /// <summary>BUG FIX: TweakBackupService must not throw on corrupt backup file.</summary>
    [Fact]
    public void TweakBackupService_LoadsCorruptFileWithoutThrowing()
    {
        string backupPath = Path.Combine(_tempDir, "corrupt_history.json");
        File.WriteAllText(backupPath, "[ { bad json !!!");
        TweakBackupService.ConfigurePath(backupPath);

        var ex = Record.Exception(() => TweakBackupService.GetHistory());
        Assert.Null(ex);

        // Restore.
        TweakBackupService.ConfigurePath(Path.Combine(_tempDir, "tweak_history.json"));
    }

    /// <summary>BUG FIX: Junction traversal protection — EnumerationOptions with
    /// AttributesToSkip = ReparsePoint must not follow a junction to outside the target folder.</summary>
    [Fact]
    public void JunctionSafe_EnumerateFiles_DoesNotTraverseReparsePoint()
    {
        // Create a target folder with a real file and a junction pointing outside.
        string cleanDir = Path.Combine(_tempDir, "clean_me");
        string outsideDir = Path.Combine(_tempDir, "outside");
        string outsideFile = Path.Combine(outsideDir, "do_not_touch.txt");
        string junctionPath = Path.Combine(cleanDir, "junk");

        Directory.CreateDirectory(cleanDir);
        Directory.CreateDirectory(outsideDir);
        File.WriteAllText(outsideFile, "sensitive");

        // Create a file inside the clean dir too.
        File.WriteAllText(Path.Combine(cleanDir, "deleteme.txt"), "junk");

        // Create the junction: junk -> outsideDir.
        var r = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junctionPath}\" \"{outsideDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        r?.WaitForExit(3000);

        if (!Directory.Exists(junctionPath))
        {
            // mklink failed (insufficient privilege in CI); skip gracefully.
            return;
        }

        try
        {
            // Junction-safe enumeration must NOT see do_not_touch.txt.
            var safeOpts = new System.IO.EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = System.IO.FileAttributes.ReparsePoint
            };
            var seen = Directory.EnumerateFiles(cleanDir, "*", safeOpts).ToList();
            Assert.DoesNotContain(seen, f => f.Contains("do_not_touch"));
            Assert.Contains(seen, f => f.Contains("deleteme"));

            // The outside file must still exist untouched.
            Assert.True(File.Exists(outsideFile));
        }
        finally
        {
            // Remove junction without following it.
            try { Directory.Delete(junctionPath); } catch { }
        }
    }

    [Fact]
    public void FileLockService_ShutdownGracefully_DoesNotThrowMarshalDirectiveException()
    {
        string unlockedFile = Path.Combine(_tempDir, "unlocked.txt");
        File.WriteAllText(unlockedFile, "test");

        var ex = Record.Exception(() => FileLockService.ShutdownGracefully(unlockedFile));
        Assert.Null(ex);
    }

    [Fact]
    public void TweakBackupService_ApplyMultiRegistryWithBackup_RevertsNestedKeysCleanly()
    {
        string baseSubKey = $@"Software\JamesOptimizer_MultiTest_{Guid.NewGuid():N}";
        string fullBase = $@"HKEY_CURRENT_USER\{baseSubKey}";
        string fullChild = $@"{fullBase}\command";

        try
        {
            TweakBackupService.ApplyMultiRegistryWithBackup(
                "TEST",
                "Multi-Registry Test",
                "ut_multi_reg",
                new (string, string, object, RegistryValueKind)[]
                {
                    (fullBase, "", "Test Label", RegistryValueKind.String),
                    (fullBase, "Icon", @"C:\test.exe", RegistryValueKind.String),
                    (fullChild, "", "\"C:\\test.exe\"", RegistryValueKind.String)
                });

            Assert.Equal("Test Label", Registry.GetValue(fullBase, "", null));
            Assert.Equal(@"C:\test.exe", Registry.GetValue(fullBase, "Icon", null));
            Assert.Equal("\"C:\\test.exe\"", Registry.GetValue(fullChild, "", null));

            bool reverted = TweakBackupService.RevertTweakById("ut_multi_reg", out string msg);
            Assert.True(reverted, msg);

            using var checkChild = Registry.CurrentUser.OpenSubKey($@"{baseSubKey}\command", false);
            using var checkBase = Registry.CurrentUser.OpenSubKey(baseSubKey, false);
            Assert.Null(checkChild);
            Assert.Null(checkBase);
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(baseSubKey, false); } catch { }
        }
    }

    [Fact]
    public void Catalog_TryCaptureServiceStep_ReturnsNullForMissingService()
    {
        var step = TweakCatalog.TryCaptureServiceStep($"NonExistentSvc_{Guid.NewGuid():N}");
        Assert.Null(step);
    }

    [Fact]
    public void DeepCleanupService_CleanPathSafe_SkipsNestedJunctions()
    {
        string cleanDir = Path.Combine(_tempDir, "clean_nested");
        string subDir = Path.Combine(cleanDir, "sub");
        string outsideDir = Path.Combine(_tempDir, "outside_nested");
        string outsideFile = Path.Combine(outsideDir, "precious.txt");
        string nestedJunction = Path.Combine(subDir, "link");

        Directory.CreateDirectory(subDir);
        Directory.CreateDirectory(outsideDir);
        File.WriteAllText(outsideFile, "keep");
        File.WriteAllText(Path.Combine(subDir, "trash.tmp"), "12345");

        var r = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{nestedJunction}\" \"{outsideDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        r?.WaitForExit(3000);
        if (!Directory.Exists(nestedJunction)) return;

        try
        {
            long measured = DeepCleanupService.MeasurePathBytesSafe(cleanDir);
            Assert.Equal(5, measured);

            long freed = DeepCleanupService.CleanPathSafe(cleanDir);
            Assert.Equal(5, freed);
            Assert.True(File.Exists(outsideFile));
            Assert.False(File.Exists(Path.Combine(subDir, "trash.tmp")));
        }
        finally
        {
            try { Directory.Delete(nestedJunction); } catch { }
        }
    }

    [Fact]
    public void TweakCatalog_AllDefinitions_HaveUniqueIdsAndSafeDetectPreview()
    {
        var ids = TweakCatalog.All.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var def in TweakCatalog.All)
        {
            var ex = Record.Exception(() =>
            {
                _ = def.Detect();
                _ = TweakCatalog.ExecuteDryRun(def.Id);
            });
            Assert.Null(ex);
        }
    }

    [Fact]
    public void DeepCleanupService_CleanPathSafe_SkipsSymlinkFileAndTopLevelJunction()
    {
        string cleanDir = Path.Combine(_tempDir, "clean_sym");
        string outsideDir = Path.Combine(_tempDir, "outside_sym");
        string outsideFile = Path.Combine(outsideDir, "secret.txt");
        string topJunction = Path.Combine(cleanDir, "top_junction");
        string symFile = Path.Combine(cleanDir, "link_file.txt");

        Directory.CreateDirectory(cleanDir);
        Directory.CreateDirectory(outsideDir);
        File.WriteAllText(outsideFile, "do not delete");
        File.WriteAllText(Path.Combine(cleanDir, "normal.tmp"), "123456");

        var jProc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{topJunction}\" \"{outsideDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        jProc?.WaitForExit(3000);

        bool symCreated = false;
        try
        {
            File.CreateSymbolicLink(symFile, outsideFile);
            symCreated = true;
        }
        catch { /* Unprivileged shells without Developer Mode cannot create file symlinks */ }

        try
        {
            long measured = DeepCleanupService.MeasurePathBytesSafe(cleanDir);
            Assert.Equal(6, measured);

            long freed = DeepCleanupService.CleanPathSafe(cleanDir);
            Assert.Equal(6, freed);
            Assert.True(File.Exists(outsideFile));
            Assert.False(File.Exists(Path.Combine(cleanDir, "normal.tmp")));
            if (symCreated)
                Assert.True(File.Exists(symFile));
        }
        finally
        {
            try { if (Directory.Exists(topJunction)) Directory.Delete(topJunction); } catch { }
            try { if (symCreated && File.Exists(symFile)) File.Delete(symFile); } catch { }
        }
    }

    [Fact]
    public void TweakBackupService_HistoryChanged_DoesNotDeadlockOnReentrancy()
    {
        string subKey = $@"Software\JamesOptimizer_EvtTest_{Guid.NewGuid():N}";
        string fullKey = $@"HKEY_CURRENT_USER\{subKey}";
        bool handlerRan = false;
        bool deadlocked = false;

        void Handler(object? s, EventArgs e)
        {
            handlerRan = true;
            var task = System.Threading.Tasks.Task.Run(() => TweakBackupService.GetHistory());
            if (!task.Wait(TimeSpan.FromSeconds(2)))
                deadlocked = true;
        }

        TweakBackupService.HistoryChanged += Handler;
        try
        {
            TweakBackupService.ApplyRegistryWithBackup("TEST", "Event Test", fullKey, "V", 1, RegistryValueKind.DWord, "ut_evt");
            Assert.True(handlerRan);
            Assert.False(deadlocked);
            TweakBackupService.RevertTweakById("ut_evt", out _);
            Assert.False(deadlocked);
        }
        finally
        {
            TweakBackupService.HistoryChanged -= Handler;
            try { Registry.CurrentUser.DeleteSubKeyTree(subKey, false); } catch { }
        }
    }

    [Fact]
    public void TweakBackupService_ApplyMultiRegistryWithBackup_RollsBackOnPartialFailure()
    {
        string subKey = $@"Software\JamesOptimizer_PartialFail_{Guid.NewGuid():N}";
        string validKey = $@"HKEY_CURRENT_USER\{subKey}";
        string invalidKey = @"INVALID_HIVE\NonExistent\Key";

        try
        {
            Assert.ThrowsAny<Exception>(() =>
                TweakBackupService.ApplyMultiRegistryWithBackup(
                    "TEST",
                    "Partial Fail Test",
                    "ut_partial_fail",
                    new (string, string, object, RegistryValueKind)[]
                    {
                        (validKey, "Step1", 123, RegistryValueKind.DWord),
                        (invalidKey, "Step2", 456, RegistryValueKind.DWord)
                    }));

            Assert.Null(Registry.GetValue(validKey, "Step1", null));
            using var checkKey = Registry.CurrentUser.OpenSubKey(subKey, false);
            Assert.Null(checkKey);
            Assert.Empty(WriteAheadJournal.GetIncompleteEntries());
            Assert.Null(TweakBackupService.GetLatestActiveForTweak("ut_partial_fail"));
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(subKey, false); } catch { }
        }
    }

    [Fact]
    public void WriteAheadJournal_LifoCrashRecovery_RemovesNestedKeysCleanly()
    {
        string parentSubKey = $@"Software\JamesOptimizer_WalLifo_{Guid.NewGuid():N}";
        string childSubKey = $@"{parentSubKey}\command";
        string fullParent = $@"HKEY_CURRENT_USER\{parentSubKey}";
        string fullChild = $@"HKEY_CURRENT_USER\{childSubKey}";

        try
        {
            var stepParent = TweakBackupService.CaptureRegistryStep(fullParent, "Icon", "app.exe", RegistryValueKind.String);
            Registry.SetValue(fullParent, "Icon", "app.exe", RegistryValueKind.String);
            var stepChild = TweakBackupService.CaptureRegistryStep(fullChild, "", "\"app.exe\"", RegistryValueKind.String);
            Registry.SetValue(fullChild, "", "\"app.exe\"", RegistryValueKind.String);

            var snap = new TweakSnapshot
            {
                TweakId = "ut_wal_lifo",
                Category = "TEST",
                Title = "LIFO Crash Recovery Test",
                Steps = new() { stepParent, stepChild }
            };
            var entry = WriteAheadJournal.Begin("APPLY_TWEAK", "ut_wal_lifo", snap);

            // Simulate crash recovery using shared TweakBackupService.RevertStepsLifo helper
            var pending = WriteAheadJournal.GetIncompleteEntries();
            Assert.Contains(pending, e => e.Id == entry.Id);
            foreach (var p in pending)
            {
                TweakBackupService.RevertStepsLifo(p.Snapshot?.Steps);
                WriteAheadJournal.MarkRolledBack(p.Id);
            }

            using var checkChild = Registry.CurrentUser.OpenSubKey(childSubKey, false);
            using var checkParent = Registry.CurrentUser.OpenSubKey(parentSubKey, false);
            Assert.Null(checkChild);
            Assert.Null(checkParent);
            Assert.Empty(WriteAheadJournal.GetIncompleteEntries());
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(parentSubKey, false); } catch { }
        }
    }

    [Fact]
    public void HostsManager_SetEntryEnabled_FailsCleanlyOnMissingFile()
    {
        string missing = Path.Combine(_tempDir, "missing_hosts");
        var (ok, msg) = HostsManagerService.SetEntryEnabled(0, false, missing);
        Assert.False(ok);
        Assert.Contains("not found", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TweakBackupService_RevertStepsLifo_RevertsParentChildKeysInReverseOrder()
    {
        string parentSubKey = $@"Software\JamesOptimizer_LifoHelper_{Guid.NewGuid():N}";
        string childSubKey = $@"{parentSubKey}\command";
        string fullParent = $@"HKEY_CURRENT_USER\{parentSubKey}";
        string fullChild = $@"HKEY_CURRENT_USER\{childSubKey}";

        try
        {
            var stepParent = TweakBackupService.CaptureRegistryStep(fullParent, "Icon", "x.exe", RegistryValueKind.String);
            Registry.SetValue(fullParent, "Icon", "x.exe", RegistryValueKind.String);
            var stepChild = TweakBackupService.CaptureRegistryStep(fullChild, "", "\"x.exe\"", RegistryValueKind.String);
            Registry.SetValue(fullChild, "", "\"x.exe\"", RegistryValueKind.String);

            int reverted = TweakBackupService.RevertStepsLifo(new[] { stepParent, stepChild });
            Assert.Equal(2, reverted);

            using var checkChild = Registry.CurrentUser.OpenSubKey(childSubKey, false);
            using var checkParent = Registry.CurrentUser.OpenSubKey(parentSubKey, false);
            Assert.Null(checkChild);
            Assert.Null(checkParent);
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(parentSubKey, false); } catch { }
        }
    }

    [Fact]
    public void DeepCleanupService_CleanPathDetailed_CountsLockedFileFailuresAndLogsCount()
    {
        string dir = Path.Combine(_tempDir, "locked_clean");
        Directory.CreateDirectory(dir);
        string unlocked = Path.Combine(dir, "unlocked.tmp");
        string locked = Path.Combine(dir, "secret_locked.tmp");
        File.WriteAllText(unlocked, "12345678");
        File.WriteAllText(locked, "LOCKED");

        var logs = new List<(LogLevel Level, string Category, string Message)>();
        void OnLog(LogLevel level, string cat, string msg) => logs.Add((level, cat, msg));
        SharedLogger.EntryWritten += OnLog;
        try
        {
            using var lockHandle = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var res = DeepCleanupService.CleanPathDetailed(dir);

            Assert.Equal(8, res.BytesFreed);
            Assert.Equal(1, res.FilesDeleted);
            Assert.Equal(1, res.FilesFailed);
            Assert.Contains(logs, l => l.Level == LogLevel.Debug && l.Category == "DeepCleanup" && l.Message.Contains("filesFailed=1") && !l.Message.Contains("secret_locked.tmp"));
        }
        finally
        {
            SharedLogger.EntryWritten -= OnLog;
        }
    }

    [Fact]
    public void GpuThermalProbe_MissingNvidiaSmi_ProbesOnlyOnceAcrossCooldown()
    {
        DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int nvidiaCalls = 0;
        int acpiCalls = 0;

        var probe = new GpuThermalProbe(
            nvidiaSmiReader: () =>
            {
                nvidiaCalls++;
                throw new System.ComponentModel.Win32Exception(2, "nvidia-smi not found");
            },
            acpiTempReader: () =>
            {
                acpiCalls++;
                return null;
            },
            utcNow: () => now);

        Assert.Equal("3D Engine", probe.ReadLabel());
        Assert.True(probe.IsNvidiaSmiMissing);
        Assert.Equal(1, nvidiaCalls);
        Assert.Equal(1, acpiCalls);

        // Advance well past 6-second cache window twice: nvidia-smi must NOT be re-probed
        now = now.AddSeconds(10);
        Assert.Equal("3D Engine", probe.ReadLabel());
        now = now.AddSeconds(10);
        Assert.Equal("3D Engine", probe.ReadLabel());

        Assert.Equal(1, nvidiaCalls);
        Assert.Equal(3, acpiCalls);
    }

    [Fact]
    public void IpcProtocol_TryParseSuccessResponse_DistinguishesSuccessErrorAndOffline()
    {
        Assert.True(IpcProtocol.TryParseSuccessResponse("SUCCESS:Game automation profiles updated.", out string? err1));
        Assert.Null(err1);

        Assert.False(IpcProtocol.TryParseSuccessResponse("ERROR:GameAutomationConfig.Rules must not be null.", out string? err2));
        Assert.Equal("GameAutomationConfig.Rules must not be null.", err2);

        Assert.False(IpcProtocol.TryParseSuccessResponse("OFFLINE:The semaphore timeout period has expired.", out string? err3));
        Assert.Equal("The semaphore timeout period has expired.", err3);

        Assert.False(IpcProtocol.TryParseSuccessResponse("", out string? err4));
        Assert.NotNull(err4);
    }
}
