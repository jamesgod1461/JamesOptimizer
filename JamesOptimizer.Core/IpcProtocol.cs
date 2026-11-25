using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace JamesOptimizer.Core;

[SupportedOSPlatform("windows")]
public static partial class IpcProtocol
{
    public const int MaxMessageLength = 8192;
    public const int MaxCommandsPerWindow = 30;
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(10);

    public static readonly IReadOnlySet<string> AllowedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "HELLO",
        "PING",
        "STATUS",
        "OPTIMIZE",
        "SET_TIMER",
        "SET_SMART_RAM",
        "GET_SMART_RAM",
        "SET_GAME_WATCH",
        "GET_GAME_WATCH",
        "SET_GAME_PROFILES",
        "GET_GAME_PROFILES"
    };

    public static bool TryParseCommand(string? raw, out string action, out string arg, out string error)
    {
        action = string.Empty;
        arg = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Empty command.";
            return false;
        }

        if (raw.Length > MaxMessageLength)
        {
            error = $"Message exceeds {MaxMessageLength} characters.";
            return false;
        }

        var parts = raw.Trim().Split(':', 2);
        string candidate = parts[0].ToUpperInvariant();
        if (!AllowedCommands.Contains(candidate))
        {
            error = $"Command '{candidate}' is not in the allow-list.";
            return false;
        }

        action = candidate;
        arg = parts.Length > 1 ? parts[1] : string.Empty;
        return true;
    }

    public static bool TryValidateTimerMinutes(string arg, out int minutes)
    {
        minutes = 0;
        if (!int.TryParse(arg, out int parsed)) return false;
        if (parsed < 0 || parsed > 1440) return false;
        minutes = parsed;
        return true;
    }

    public static bool TrySanitizeGameList(string raw, out string sanitized)
    {
        sanitized = string.Empty;
        if (raw.Length > 2048) return false;
        var items = raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cleanList = new List<string>();
        foreach (string item in items)
        {
            string trimmed = item.Trim();
            string clean = trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? trimmed[..^4].Trim()
                : trimmed;
            if (clean.Length is >= 1 and <= 64 && SafeProcessNameRegex().IsMatch(clean))
                cleanList.Add(clean);
        }
        sanitized = string.Join(';', cleanList);
        return true;
    }

    public static bool VerifyPipeClient(NamedPipeServerStream server, out string reason)
    {
        reason = string.Empty;
        try
        {
            bool pipeHandleRefAdded = false;
            uint pid = 0;
            bool gotPid;
            try
            {
                server.SafePipeHandle.DangerousAddRef(ref pipeHandleRefAdded);
                IntPtr handle = server.SafePipeHandle.DangerousGetHandle();
                gotPid = GetNamedPipeClientProcessId(handle, out pid);
            }
            finally
            {
                if (pipeHandleRefAdded)
                    server.SafePipeHandle.DangerousRelease();
            }

            if (!gotPid || pid == 0)
            {
                reason = "Could not resolve pipe client PID.";
                return false;
            }

            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            string? exePath = proc.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                reason = $"Client PID {pid} has no accessible MainModule path.";
                return false;
            }

            string fileName = Path.GetFileName(exePath);
#if DEBUG
            if (!fileName.Equals("JamesOptimizer.WinUI.exe", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Equals("testhost.exe", StringComparison.OrdinalIgnoreCase))
#else
            if (!fileName.Equals("JamesOptimizer.WinUI.exe", StringComparison.OrdinalIgnoreCase))
#endif
            {
                reason = $"Unauthorized client binary '{fileName}'.";
                return false;
            }

#if !DEBUG
            if (!NativeSystemHelper.VerifyAuthenticodeSignature(exePath, out string? subject) ||
                string.IsNullOrWhiteSpace(subject) ||
                !subject.Contains(CoreConstants.PublisherName, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Client binary '{exePath}' failed Authenticode verification.";
                return false;
            }
#endif
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Client verification error: {ex.Message}";
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr Pipe, out uint ClientProcessId);

    [GeneratedRegex(@"^[a-zA-Z0-9_\-\.\s]+$")]
    private static partial Regex SafeProcessNameRegex();
}
