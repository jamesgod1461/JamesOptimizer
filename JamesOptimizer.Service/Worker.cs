using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace JamesOptimizer.Service;

[SupportedOSPlatform("windows")]
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly object _rateLock = new();
    private readonly object _gameWatchLock = new();
    private DateTime _rateWindowStart = DateTime.UtcNow;
    private int _rateWindowCount = 0;

    private DateTime _lastOptimizationTime = DateTime.MinValue;
    private DateTime _lastSmartPurgeTime = DateTime.MinValue;
    private readonly Queue<DateTime> _smartPurgeHistory = new();

    private volatile bool _inAutoGameMode = false;
    private string _activeGameProcess = "";
    private int _activeGamePid = 0;
    private string _preGamePowerGuid = CoreConstants.BalancedGuid;
    private readonly List<string> _suspendedForGame = new();

    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        SharedLogger.ConfigureForService();
        WriteAheadJournal.ConfigurePath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            CoreConstants.ProductName, "backups", "service_journal.json"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JamesOptimizer Background Service v{Version} started.", CoreConstants.ProtocolVersion);
        SharedLogger.Info("SERVICE", $"Background Service v{CoreConstants.ProtocolVersion} started.");

        RecoverInterruptedGameState();

        _ = Task.Run(() => ListenToNamedPipeAsync(stoppingToken), stoppingToken);
        _ = Task.Run(() => WatchGamesAsync(stoppingToken), stoppingToken);
        _ = Task.Run(() => SmartRamLoopAsync(stoppingToken), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            int intervalMinutes = 0;
            try
            {
                object? regValue = Registry.GetValue(CoreConstants.RegistryConfigRoot, "AutoTimer", 0);
                if (regValue != null)
                    intervalMinutes = Math.Clamp(Convert.ToInt32(regValue), 0, 1440);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading registry for AutoTimer.");
            }

            if (intervalMinutes > 0)
            {
                try
                {
                    await RunRamFlushAsync(stoppingToken);
                    await RunFastCleanAsync(stoppingToken);
                    _lastOptimizationTime = DateTime.Now;
                    SharedLogger.Info("SERVICE", $"Scheduled AutoTimer ({intervalMinutes}m) optimization cycle completed.");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    SharedLogger.Error("SERVICE", "Scheduled optimization cycle failed", ex);
                }

                try { await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
            else
            {
                try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        if (_inAutoGameMode)
            ExitGameProfile();
        CleanupWatchers();
    }

    private void RecoverInterruptedGameState()
    {
        try
        {
            var pending = WriteAheadJournal.GetIncompleteEntries();
            foreach (var entry in pending)
            {
                if (entry.Operation == "GAME_PROFILE" && entry.Snapshot != null)
                {
                    SharedLogger.Warn("SERVICE", $"Recovering interrupted game profile '{entry.TweakId}' after service restart.");
                    TweakBackupService.RevertStepsLifo(entry.Snapshot.Steps);
                    WriteAheadJournal.MarkRolledBack(entry.Id);
                }
            }
        }
        catch (Exception ex)
        {
            SharedLogger.Error("SERVICE", "Failed recovering interrupted game state", ex);
        }
    }

    private async Task ListenToNamedPipeAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Named Pipe server listening on {PipeName}", CoreConstants.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var server = CreateSecuredPipeServer();
                await server.WaitForConnectionAsync(stoppingToken);

                if (!IpcProtocol.VerifyPipeClient(server, out string denyReason))
                {
                    SharedLogger.Warn("IPC", $"Rejected pipe client: {denyReason}");
                    continue;
                }

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true, bufferSize: 4096);
                using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                // BUG FIX: Bound pipe reads with a per-connection timeout so a stalled client cannot wedge the pipe server.
                using var readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                readTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                string? request;
                try
                {
                    request = await ReadBoundedLineAsync(reader, IpcProtocol.MaxMessageLength, readTimeoutCts.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    SharedLogger.Warn("IPC", "Pipe client read timed out.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(request)) continue;

                if (!CheckRateLimit())
                {
                    SharedLogger.Warn("IPC", "Rate limit exceeded on Named Pipe.");
                    await writer.WriteLineAsync("ERROR:Rate limit exceeded.".AsMemory(), stoppingToken);
                    continue;
                }

                if (!IpcProtocol.TryParseCommand(request, out string action, out string arg, out string parseError))
                {
                    SharedLogger.Warn("IPC", $"Rejected invalid command '{request[..Math.Min(request.Length, 80)]}': {parseError}");
                    await writer.WriteLineAsync($"ERROR:{parseError}".AsMemory(), stoppingToken);
                    continue;
                }

                string response = await ProcessIpcCommandAsync(action, arg, stoppingToken);
                await writer.WriteLineAsync(response.AsMemory(), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                SharedLogger.Error("IPC", "Named Pipe loop error", ex);
                await Task.Delay(500, stoppingToken);
            }
        }
    }

    private static NamedPipeServerStream CreateSecuredPipeServer()
    {
        var pipeSecurity = new PipeSecurity();

        // Explicitly deny network access so remote SMB clients can never connect
        var networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(networkSid, PipeAccessRights.FullControl, AccessControlType.Deny));

        // Allow LocalSystem + Administrators (note: PipeOptions.CurrentUserOnly is intentionally omitted because Service=LocalSystem and UI=Admin user)
        var localSystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(localSystemSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            CoreConstants.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0, 0,
            pipeSecurity);
    }

    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, int maxChars, CancellationToken ct)
    {
        var sb = new StringBuilder();
        char[] buf = new char[1];
        while (sb.Length <= maxChars)
        {
            int read = await reader.ReadAsync(buf, ct);
            if (read == 0) break;
            if (buf[0] == '\n') break;
            if (buf[0] != '\r') sb.Append(buf[0]);
        }
        return sb.Length > maxChars ? sb.ToString(0, maxChars + 1) : sb.ToString();
    }

    private bool CheckRateLimit()
    {
        lock (_rateLock)
        {
            var now = DateTime.UtcNow;
            if (now - _rateWindowStart > IpcProtocol.RateLimitWindow)
            {
                _rateWindowStart = now;
                _rateWindowCount = 0;
            }
            _rateWindowCount++;
            return _rateWindowCount <= IpcProtocol.MaxCommandsPerWindow;
        }
    }

    private async Task<string> ProcessIpcCommandAsync(string action, string arg, CancellationToken stoppingToken)
    {
        switch (action)
        {
            case "HELLO":
                if (string.IsNullOrWhiteSpace(arg))
                    return $"HELLO:ERROR:Missing version";
                bool compatible = arg.StartsWith("3.", StringComparison.Ordinal);
                return compatible
                    ? $"HELLO:OK:{CoreConstants.ProtocolVersion}"
                    : $"HELLO:MISMATCH:Service={CoreConstants.ProtocolVersion},UI={arg}";

            case "PING":
                return $"PONG:JamesOptimizer.Service v{CoreConstants.ProtocolVersion} Active";

            case "STATUS":
                int timer = 0;
                try
                {
                    var val = Registry.GetValue(CoreConstants.RegistryConfigRoot, "AutoTimer", 0);
                    if (val != null) timer = Convert.ToInt32(val);
                }
                catch { }
                return $"STATUS:Version={CoreConstants.ProtocolVersion}|AutoTimer={timer}|LastRun={_lastOptimizationTime:HH:mm:ss}|PID={Environment.ProcessId}|GameMode={_inAutoGameMode}";

            case "OPTIMIZE":
                try
                {
                    await RunRamFlushAsync(stoppingToken);
                    await RunFastCleanAsync(stoppingToken);
                    _lastOptimizationTime = DateTime.Now;
                    return "SUCCESS:Optimization executed via Background Service.";
                }
                catch (Exception ex) { return $"ERROR:{ex.Message}"; }

            case "SET_TIMER":
                if (!IpcProtocol.TryValidateTimerMinutes(arg, out int minutes))
                {
                    SharedLogger.Warn("IPC", $"Rejected SET_TIMER with invalid argument '{arg}'.");
                    return "ERROR:Invalid minutes argument (0-1440 allowed).";
                }
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\JamesOptimizer", true);
                    key?.SetValue("AutoTimer", minutes, RegistryValueKind.DWord);
                    return $"SUCCESS:AutoTimer updated to {minutes}m.";
                }
                catch (Exception ex) { return $"ERROR:Registry write failed: {ex.Message}"; }

            case "SET_SMART_RAM":
                try
                {
                    var settings = JsonSerializer.Deserialize<SmartRamSettings>(arg);
                    if (settings == null) return "ERROR:Invalid SmartRamSettings payload.";
                    settings.FreeRamThresholdMb = Math.Clamp(settings.FreeRamThresholdMb, 256, 32768);
                    settings.MinStandbyMb = Math.Clamp(settings.MinStandbyMb, 128, 32768);
                    settings.CooldownSeconds = Math.Clamp(settings.CooldownSeconds, 30, 3600);
                    settings.MaxPurgesPerHour = Math.Clamp(settings.MaxPurgesPerHour, 1, 30);

                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\JamesOptimizer", true);
                    key?.SetValue("SmartRamJson", JsonSerializer.Serialize(settings), RegistryValueKind.String);
                    return "SUCCESS:SmartRam settings saved.";
                }
                catch (Exception ex) { return $"ERROR:{ex.Message}"; }

            case "GET_SMART_RAM":
                return "SMARTRAM:" + JsonSerializer.Serialize(LoadSmartRamSettings());

            case "SET_GAME_WATCH":
                try
                {
                    var cfg = arg.Split('|', 2);
                    int enabled = cfg.Length > 0 && cfg[0] == "1" ? 1 : 0;
                    string rawGames = cfg.Length > 1 ? cfg[1] : "";
                    if (!IpcProtocol.TrySanitizeGameList(rawGames, out string cleanGames))
                        return "ERROR:Invalid watched game list.";

                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\JamesOptimizer", true);
                    key?.SetValue("AutoGameWatch", enabled, RegistryValueKind.DWord);
                    key?.SetValue("WatchedGames", cleanGames, RegistryValueKind.String);
                    return $"SUCCESS:AutoGameWatch={enabled}";
                }
                catch (Exception ex) { return $"ERROR:{ex.Message}"; }

            case "GET_GAME_WATCH":
                try
                {
                    int en = Convert.ToInt32(Registry.GetValue(CoreConstants.RegistryConfigRoot, "AutoGameWatch", 0) ?? 0);
                    string gms = Registry.GetValue(CoreConstants.RegistryConfigRoot, "WatchedGames", "cs2;valorant;fortnite")?.ToString() ?? "";
                    return $"GAMEWATCH:{en}|{gms}|Active={_inAutoGameMode}|Game={_activeGameProcess}";
                }
                catch (Exception ex) { return $"ERROR:{ex.Message}"; }

            case "SET_GAME_PROFILES":
                try
                {
                    var gcfg = JsonSerializer.Deserialize<GameAutomationConfig>(arg);
                    if (gcfg == null) return "ERROR:Invalid GameAutomationConfig JSON.";
                    // BUG FIX: gcfg.Rules may be null from JSON (missing field); guard prevents NullReferenceException.
                    if (gcfg.Rules == null) return "ERROR:GameAutomationConfig.Rules must not be null.";
                    foreach (var r in gcfg.Rules)
                    {
                        r.TemporarilySuspendServices.RemoveAll(CoreConstants.IsProtectedService);
                        if (!string.Equals(r.ProcessPriority, "Normal", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(r.ProcessPriority, "AboveNormal", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(r.ProcessPriority, "High", StringComparison.OrdinalIgnoreCase))
                        {
                            r.ProcessPriority = "High";
                        }
                        if (!string.IsNullOrWhiteSpace(r.PowerPlanGuid) && !Guid.TryParse(r.PowerPlanGuid, out _))
                        {
                            r.PowerPlanGuid = CoreConstants.UltimatePerformanceGuid;
                        }
                    }
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\JamesOptimizer", true);
                    key?.SetValue("GameAutomationJson", JsonSerializer.Serialize(gcfg), RegistryValueKind.String);
                    key?.SetValue("AutoGameWatch", gcfg.Enabled ? 1 : 0, RegistryValueKind.DWord);
                    return "SUCCESS:Game automation profiles updated.";
                }
                catch (Exception ex) { return $"ERROR:{ex.Message}"; }

            case "GET_GAME_PROFILES":
                return "GAMEPROFILES:" + JsonSerializer.Serialize(LoadGameAutomationConfig());

            default:
                return "ERROR:Unknown command.";
        }
    }

    private static SmartRamSettings LoadSmartRamSettings()
    {
        try
        {
            string? json = Registry.GetValue(CoreConstants.RegistryConfigRoot, "SmartRamJson", null)?.ToString();
            if (!string.IsNullOrWhiteSpace(json))
            {
                var parsed = JsonSerializer.Deserialize<SmartRamSettings>(json);
                if (parsed != null) return parsed;
            }
        }
        catch { }
        return new SmartRamSettings();
    }

    private static GameAutomationConfig LoadGameAutomationConfig()
    {
        try
        {
            string? json = Registry.GetValue(CoreConstants.RegistryConfigRoot, "GameAutomationJson", null)?.ToString();
            if (!string.IsNullOrWhiteSpace(json))
            {
                var parsed = JsonSerializer.Deserialize<GameAutomationConfig>(json);
                if (parsed != null) return parsed;
            }

            int enabled = Convert.ToInt32(Registry.GetValue(CoreConstants.RegistryConfigRoot, "AutoGameWatch", 0) ?? 0);
            string raw = Registry.GetValue(CoreConstants.RegistryConfigRoot, "WatchedGames", "")?.ToString() ?? "";
            var cfg = new GameAutomationConfig { Enabled = enabled == 1 };
            if (!string.IsNullOrWhiteSpace(raw))
            {
                cfg.Rules.Clear();
                foreach (string item in raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string clean = item.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                    cfg.Rules.Add(new GameAutomationRule { ProcessName = clean, DisplayName = clean });
                }
            }
            return cfg;
        }
        catch
        {
            return new GameAutomationConfig();
        }
    }

    private async Task SmartRamLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = LoadSmartRamSettings();
                if (settings.Enabled && !_inAutoGameMode)
                {
                    if (settings.PauseDuringFullscreen && IsUserInFullscreenOrPresentation())
                    {
                        await Task.Delay(15000, stoppingToken);
                        continue;
                    }

                    var now = DateTime.UtcNow;
                    while (_smartPurgeHistory.Count > 0 && now - _smartPurgeHistory.Peek() > TimeSpan.FromHours(1))
                        _smartPurgeHistory.Dequeue();

                    bool cooldownOk = (now - _lastSmartPurgeTime).TotalSeconds >= settings.CooldownSeconds;
                    bool capOk = _smartPurgeHistory.Count < settings.MaxPurgesPerHour;

                    if (cooldownOk && capOk)
                    {
                        var mem = NativeSystemHelper.GetMemoryBreakdown();
                        if (mem.AvailablePhysicalMb < settings.FreeRamThresholdMb && mem.StandbyEstimateMb >= settings.MinStandbyMb)
                        {
                            var (_, reclaimed) = NativeSystemHelper.PurgeMemoryListsNative(
                                emptyWorkingSets: false, flushModified: false, purgeStandby: true, ct: stoppingToken);
                            _lastSmartPurgeTime = now;
                            _smartPurgeHistory.Enqueue(now);

                            double reclaimedMb = reclaimed / (1024.0 * 1024.0);
                            SharedLogger.Info("SMARTRAM",
                                $"Standby purged (Free was {mem.AvailablePhysicalMb:F0} MB < {settings.FreeRamThresholdMb} MB). Reclaimed {reclaimedMb:F1} MB.");
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                SharedLogger.Error("SMARTRAM", "Smart RAM check failed", ex);
            }

            try { await Task.Delay(15000, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task WatchGamesAsync(CancellationToken stoppingToken)
    {
        TryInitializeWmiProcessWatchers();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cfg = LoadGameAutomationConfig();
                if (cfg.Enabled && cfg.Rules.Count > 0)
                {
                    EvaluateRunningGames(cfg);
                }
                else if (_inAutoGameMode)
                {
                    ExitGameProfile();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                SharedLogger.Error("GAMEWATCH", "WatchGamesAsync tick failed", ex);
            }

            try { await Task.Delay(5000, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void TryInitializeWmiProcessWatchers()
    {
        try
        {
            _startWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessName, ProcessID FROM Win32_ProcessStartTrace"));
            _startWatcher.EventArrived += (_, args) =>
            {
                try
                {
                    string name = (args.NewEvent["ProcessName"]?.ToString() ?? "").Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                    int pid = Convert.ToInt32(args.NewEvent["ProcessID"]);
                    var cfg = LoadGameAutomationConfig();
                    if (!cfg.Enabled || _inAutoGameMode) return;
                    var rule = cfg.Rules.Find(r => r.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (rule != null)
                        EngageGameProfile(rule, pid);
                }
                catch { }
            };
            _startWatcher.Start();

            _stopWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessName, ProcessID FROM Win32_ProcessStopTrace"));
            _stopWatcher.EventArrived += (_, args) =>
            {
                try
                {
                    if (!_inAutoGameMode) return;
                    int pid = Convert.ToInt32(args.NewEvent["ProcessID"]);
                    if (pid == _activeGamePid)
                        ExitGameProfile();
                }
                catch { }
            };
            _stopWatcher.Start();
            SharedLogger.Info("GAMEWATCH", "WMI process-trace watchers engaged.");
        }
        catch (Exception ex)
        {
            SharedLogger.Warn("GAMEWATCH", $"WMI process-trace unavailable, using polling fallback ({ex.Message}).");
            if (_inAutoGameMode)
            ExitGameProfile();
        CleanupWatchers();
        }
    }

    private void CleanupWatchers()
    {
        try { _startWatcher?.Stop(); _startWatcher?.Dispose(); } catch { }
        try { _stopWatcher?.Stop(); _stopWatcher?.Dispose(); } catch { }
        _startWatcher = null;
        _stopWatcher = null;
    }

    private void EvaluateRunningGames(GameAutomationConfig cfg)
    {
        Process? matchedProc = null;
        GameAutomationRule? matchedRule = null;

        var processes = Process.GetProcesses();
        try
        {
            foreach (var proc in processes)
            {
                try
                {
                    foreach (var rule in cfg.Rules)
                    {
                        string clean = rule.ProcessName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                        if (proc.ProcessName.Equals(clean, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedProc = proc;
                            matchedRule = rule;
                            break;
                        }
                    }
                    if (matchedProc != null) break;
                }
                catch { }
            }

            if (matchedProc != null && matchedRule != null && !_inAutoGameMode)
            {
                EngageGameProfile(matchedRule, matchedProc.Id);
            }
            else if (matchedProc == null && _inAutoGameMode)
            {
                ExitGameProfile();
            }
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    private void EngageGameProfile(GameAutomationRule rule, int pid)
    {
        lock (_gameWatchLock)
        {
            if (_inAutoGameMode) return;
            _inAutoGameMode = true;
            _activeGameProcess = rule.ProcessName;
            _activeGamePid = pid;

            var activeRes = NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/getactivescheme" });
            var m = System.Text.RegularExpressions.Regex.Match(activeRes.StdOut, @"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}");
            _preGamePowerGuid = m.Success ? m.Value : CoreConstants.BalancedGuid;

            var steps = new List<SnapshotStep>
            {
                new() { Kind = SnapshotStepKind.PowerPlan, OriginalValueText = _preGamePowerGuid }
            };

            _suspendedForGame.Clear();
            foreach (string svc in rule.TemporarilySuspendServices)
            {
                if (CoreConstants.IsProtectedService(svc)) continue;
                try
                {
                    using var sc = new ServiceController(svc);
                    if (sc.CanStop && sc.Status == ServiceControllerStatus.Running)
                    {
                        steps.Add(new SnapshotStep
                        {
                            Kind = SnapshotStepKind.WindowsService,
                            ServiceName = svc,
                            OriginalStartMode = sc.StartType.ToString(),
                            OriginalRunning = true
                        });
                    }
                }
                catch { }
            }

            var snap = new TweakSnapshot
            {
                TweakId = $"game_{rule.ProcessName}",
                Category = "GAMING",
                Title = $"Game Profile ({rule.ProcessName})",
                Steps = steps
            };
            var journal = WriteAheadJournal.Begin("GAME_PROFILE", rule.ProcessName, snap);

            try
            {
                string targetGuid = string.IsNullOrWhiteSpace(rule.PowerPlanGuid) ? CoreConstants.UltimatePerformanceGuid : rule.PowerPlanGuid;
                NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/setactive", targetGuid });

                try
                {
                    using var gameProc = Process.GetProcessById(pid);
                    if (!CoreConstants.IsProtectedProcess(gameProc.ProcessName))
                    {
                        gameProc.PriorityClass = rule.ProcessPriority switch
                        {
                            "AboveNormal" => ProcessPriorityClass.AboveNormal,
                            "High" => ProcessPriorityClass.High,
                            _ => ProcessPriorityClass.Normal
                        };
                        if (!string.IsNullOrWhiteSpace(rule.CpuAffinityMaskHex) &&
                            long.TryParse(rule.CpuAffinityMaskHex, System.Globalization.NumberStyles.HexNumber, null, out long mask) &&
                            mask > 0)
                        {
                            gameProc.ProcessorAffinity = (IntPtr)mask;
                        }
                    }
                }
                catch { }

                foreach (var step in steps)
                {
                    if (step.Kind != SnapshotStepKind.WindowsService) continue;
                    try
                    {
                        using var sc = new ServiceController(step.ServiceName);
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(4));
                        _suspendedForGame.Add(step.ServiceName);
                    }
                    catch { }
                }

                if (rule.PurgeStandbyOnLaunch)
                {
                    NativeSystemHelper.PurgeMemoryListsNative(emptyWorkingSets: false, flushModified: false, purgeStandby: true);
                }

                SharedLogger.Info("GAMEWATCH", $"Engaged profile for '{rule.ProcessName}' (PID {pid}).");
            }
            catch (Exception ex)
            {
                WriteAheadJournal.MarkFailed(journal.Id, ex.Message);
                SharedLogger.Error("GAMEWATCH", $"Failed engaging profile for '{rule.ProcessName}'", ex);
            }
        }
    }

    private void ExitGameProfile()
    {
        lock (_gameWatchLock)
        {
            if (!_inAutoGameMode) return;
            string game = _activeGameProcess;
            _inAutoGameMode = false;
            _activeGameProcess = "";
            _activeGamePid = 0;

            try
            {
                NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/setactive", _preGamePowerGuid });
                foreach (string svc in _suspendedForGame)
                {
                    if (CoreConstants.IsProtectedService(svc)) continue;
                    try
                    {
                        using var sc = new ServiceController(svc);
                        if (sc.Status != ServiceControllerStatus.Running)
                            sc.Start();
                    }
                    catch { }
                }
                _suspendedForGame.Clear();

                foreach (var entry in WriteAheadJournal.GetIncompleteEntries())
                {
                    if (entry.Operation == "GAME_PROFILE")
                        WriteAheadJournal.MarkRolledBack(entry.Id);
                }

                SharedLogger.Info("GAMEWATCH", $"Game '{game}' exited. Restored power plan {_preGamePowerGuid} and suspended services.");
            }
            catch (Exception ex)
            {
                SharedLogger.Error("GAMEWATCH", "ExitGameProfile failed", ex);
            }
        }
    }

    private static async Task RunRamFlushAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            NativeSystemHelper.PurgeMemoryListsNative(emptyWorkingSets: true, flushModified: false, purgeStandby: true, ct: cancellationToken);
        }, cancellationToken);
    }

    private static async Task RunFastCleanAsync(CancellationToken cancellationToken)
    {
        // Note: C:\Windows\Prefetch is intentionally excluded so cold-launch caching is preserved.
        var r1 = await Task.Run(() => DeepCleanupService.CleanPathDetailed(Path.GetTempPath(), cancellationToken), cancellationToken);
        var r2 = await Task.Run(() => DeepCleanupService.CleanPathDetailed(@"C:\Windows\Temp", cancellationToken), cancellationToken);
        int failedFiles = r1.FilesFailed + r2.FilesFailed;
        int failedDirs = r1.DirsFailed + r2.DirsFailed;
        if (failedFiles > 0 || failedDirs > 0)
            SharedLogger.Debug("CLEANUP", $"Auto fast-clean skipped locked items: files={failedFiles}, dirs={failedDirs}.");
    }

    private static bool IsUserInFullscreenOrPresentation()
    {
        try
        {
            if (SHQueryUserNotificationState(out int state) == 0)
                return state == 2 || state == 3 || state == 4; // QUNS_BUSY, QUNS_RUNNING_D3D_FULL_SCREEN, QUNS_PRESENTATION_MODE
        }
        catch { }
        return false;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int pquns);
}
