using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JamesOptimizer.Core;

namespace JamesOptimizer.WinUI.Services;

public readonly record struct ServiceCommandResult(bool Success, string? Error, bool IsOffline = false);

public class BackgroundServiceClient
{
    public static async Task<string> SendCommandAsync(string command, int timeoutMs = 1500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", CoreConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(timeoutMs);

            await client.ConnectAsync(cts.Token);

            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync(command.AsMemory(), cts.Token);
            string? response = await reader.ReadLineAsync(cts.Token);

            return response ?? "NO_RESPONSE";
        }
        catch (Exception ex)
        {
            return $"OFFLINE:{ex.Message}";
        }
    }

    public static async Task<(bool Compatible, string Message)> HelloHandshakeAsync()
    {
        var res = await SendCommandAsync($"HELLO:{CoreConstants.ProtocolVersion}", 1000);
        if (res.StartsWith("HELLO:OK:", StringComparison.OrdinalIgnoreCase))
            return (true, $"Connected (v{res[9..]})");
        if (res.StartsWith("HELLO:MISMATCH:", StringComparison.OrdinalIgnoreCase))
            return (false, $"Version mismatch: {res[15..]}");
        return (false, res);
    }

    public static Task<bool> PingAsync() => IsServiceOnlineAsync();

    public static async Task<bool> IsServiceOnlineAsync()
    {
        var res = await SendCommandAsync("PING", 800);
        return res.StartsWith("PONG", StringComparison.OrdinalIgnoreCase);
    }

    public static Task<string> GetStatusAsync() => SendCommandAsync("STATUS", 1000);

    public static Task<string> RequestOptimizationAsync() => SendCommandAsync("OPTIMIZE", 10000);

    public static async Task<ServiceCommandResult> SetAutoTimerAsync(int minutes)
    {
        var res = await SendCommandAsync($"SET_TIMER:{minutes}", 2000);
        return ParseCommandResult(res);
    }

    public static async Task<ServiceCommandResult> SetGameWatchAsync(bool enabled, string gamesCsv)
    {
        var res = await SendCommandAsync($"SET_GAME_WATCH:{(enabled ? "1" : "0")}|{gamesCsv}", 2000);
        return ParseCommandResult(res);
    }

    public static async Task<SmartRamSettings?> GetSmartRamSettingsAsync()
    {
        var res = await SendCommandAsync("GET_SMART_RAM", 1500);
        if (!res.StartsWith("SMARTRAM:", StringComparison.OrdinalIgnoreCase)) return null;
        try { return JsonSerializer.Deserialize<SmartRamSettings>(res[9..]); } catch { return null; }
    }

    public static async Task<ServiceCommandResult> SetSmartRamSettingsAsync(SmartRamSettings settings)
    {
        var res = await SendCommandAsync("SET_SMART_RAM:" + JsonSerializer.Serialize(settings), 2000);
        return ParseCommandResult(res);
    }

    public static async Task<GameAutomationConfig?> GetGameProfilesAsync()
    {
        var res = await SendCommandAsync("GET_GAME_PROFILES", 1500);
        if (!res.StartsWith("GAMEPROFILES:", StringComparison.OrdinalIgnoreCase)) return null;
        try { return JsonSerializer.Deserialize<GameAutomationConfig>(res[13..]); } catch { return null; }
    }

    public static async Task<ServiceCommandResult> SetGameProfilesAsync(GameAutomationConfig cfg)
    {
        var res = await SendCommandAsync("SET_GAME_PROFILES:" + JsonSerializer.Serialize(cfg), 2000);
        return ParseCommandResult(res);
    }

    private static ServiceCommandResult ParseCommandResult(string res)
    {
        bool offline = res.StartsWith("OFFLINE:", StringComparison.OrdinalIgnoreCase) ||
                       res.Equals("NO_RESPONSE", StringComparison.OrdinalIgnoreCase);
        bool ok = IpcProtocol.TryParseSuccessResponse(res, out string? error);
        return new ServiceCommandResult(ok, error, offline);
    }
}
