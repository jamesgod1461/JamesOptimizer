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

    public static string EvaluateHello(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
            return "HELLO:ERROR:Missing version";

        string trimmed = arg.Trim();
        string expectedComposite = $"{CoreConstants.AppVersion}|{CoreConstants.ProtocolVersion}";
        bool compatible = string.Equals(trimmed, expectedComposite, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(trimmed, CoreConstants.ProtocolVersion, StringComparison.OrdinalIgnoreCase);

        return compatible
            ? $"HELLO:OK:{expectedComposite}"
            : $"HELLO:MISMATCH:Service={expectedComposite},UI={trimmed}";
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

    public static bool TryParseSuccessResponse(string? response, out string? error)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            error = "No response from background service.";
            return false;
        }
        if (response.StartsWith("SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            error = null;
            return true;
        }
        if (response.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
        {
            error = response[6..].Trim();
            return false;
        }
        if (response.StartsWith("OFFLINE:", StringComparison.OrdinalIgnoreCase))
        {
            error = response[8..].Trim();
            return false;
        }
        error = response;
        return false;
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
            NativeSystemHelper.VerifyAuthenticodeDetails(exePath, out int clientTrustStatus, out string? clientSubject, out string? clientThumbprint);
            string? servicePath = Environment.ProcessPath;
            string? serviceThumbprint = null;
            if (!string.IsNullOrWhiteSpace(servicePath))
                NativeSystemHelper.VerifyAuthenticodeDetails(servicePath, out _, out _, out serviceThumbprint);

            if (!IsTrustedPipeClientSignature(clientTrustStatus, clientSubject, clientThumbprint, exePath, serviceThumbprint, servicePath))
            {
                reason = $"Client binary '{exePath}' failed Authenticode verification (WinVerifyTrust=0x{clientTrustStatus:X8}).";
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

    /// <summary>
    /// Evaluates whether a pipe client's Authenticode verification satisfies Release IPC security rules:
    /// either full Windows root-chain trust (status 0) for the expected publisher, or an intact self-signed
    /// digest (CERT_E_UNTRUSTEDROOT) whose leaf thumbprint matches the running service binary in the same install tree.
    /// </summary>
    public static bool IsTrustedPipeClientSignature(
        int clientTrustStatus,
        string? clientSubject,
        string? clientThumbprint,
        string clientExePath,
        string? serviceThumbprint,
        string? serviceExePath)
    {
        if (string.IsNullOrWhiteSpace(clientSubject) ||
            !clientSubject.Contains(CoreConstants.PublisherName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (clientTrustStatus == 0)
            return true;

        const int CertE_UntrustedRoot = unchecked((int)0x800B0109);
        if (clientTrustStatus != CertE_UntrustedRoot)
            return false;

        if (string.IsNullOrWhiteSpace(clientThumbprint) ||
            string.IsNullOrWhiteSpace(serviceThumbprint) ||
            !string.Equals(clientThumbprint, serviceThumbprint, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(clientExePath) || string.IsNullOrWhiteSpace(serviceExePath))
            return false;

        try
        {
            string? clientDir = Path.GetDirectoryName(Path.GetFullPath(clientExePath));
            string? serviceDir = Path.GetDirectoryName(Path.GetFullPath(serviceExePath));
            if (string.IsNullOrWhiteSpace(clientDir) || string.IsNullOrWhiteSpace(serviceDir))
                return false;

            string serviceParentDir = Directory.GetParent(serviceDir)?.FullName ?? serviceDir;
            return string.Equals(clientDir, serviceDir, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(clientDir, serviceParentDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr Pipe, out uint ClientProcessId);

    [GeneratedRegex(@"^[a-zA-Z0-9_\-\.\s]+$")]
    private static partial Regex SafeProcessNameRegex();
}

/// <summary>
/// Fixed-window rate limiter protecting Named Pipe command handling from flooding.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IpcRateLimiter
{
    private readonly object _lock = new();
    private readonly int _maxCommandsPerWindow;
    private readonly TimeSpan _window;
    private readonly Func<DateTime> _utcNow;
    private DateTime _windowStart;
    private int _count;

    public IpcRateLimiter(
        int maxCommandsPerWindow = IpcProtocol.MaxCommandsPerWindow,
        TimeSpan? window = null,
        Func<DateTime>? utcNow = null)
    {
        _maxCommandsPerWindow = maxCommandsPerWindow;
        _window = window ?? IpcProtocol.RateLimitWindow;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _windowStart = _utcNow();
    }

    public bool TryAcquire()
    {
        lock (_lock)
        {
            var now = _utcNow();
            if (now - _windowStart > _window)
            {
                _windowStart = now;
                _count = 0;
            }
            _count++;
            return _count <= _maxCommandsPerWindow;
        }
    }
}
