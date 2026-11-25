using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JamesOptimizer.Core;

public sealed class WingetPackageItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Utilities";
    public string Description { get; set; } = "";
    public bool IsInstalled { get; set; }
    public bool IsSelected { get; set; }
}

[SupportedOSPlatform("windows")]
public static class WingetInstallerService
{
    private static readonly List<WingetPackageItem> DefaultCatalog = new()
    {
        new() { Id = "7zip.7zip", Name = "7-Zip", Category = "Utilities", Description = "High-compression open-source file archiver." },
        new() { Id = "Notepad++.Notepad++", Name = "Notepad++", Category = "Utilities", Description = "Fast native text and source code editor." },
        new() { Id = "voidtools.Everything", Name = "Everything Search", Category = "Utilities", Description = "Instant NTFS file name search engine." },
        new() { Id = "CPUID.CPU-Z", Name = "CPU-Z", Category = "Diagnostics", Description = "Detailed CPU, cache, motherboard, and SPD memory telemetry." },
        new() { Id = "TechPowerUp.GPU-Z", Name = "GPU-Z", Category = "Diagnostics", Description = "Graphics card specifications, sensors, and BIOS readout." },
        new() { Id = "AntibodySoftware.WizTree", Name = "WizTree", Category = "Diagnostics", Description = "High-speed NTFS MFT disk space analyzer." },
        new() { Id = "Mozilla.Firefox", Name = "Mozilla Firefox", Category = "Browsers", Description = "Open-source privacy-focused web browser." },
        new() { Id = "Brave.Brave", Name = "Brave Browser", Category = "Browsers", Description = "Chromium browser with built-in ad & tracker blocking." },
        new() { Id = "Microsoft.VCRedist.2015+.x64", Name = "Visual C++ 2015-2022 Redist (x64)", Category = "Runtimes", Description = "Required runtime libraries for most PC games and native apps." },
        new() { Id = "Microsoft.DotNet.DesktopRuntime.10", Name = ".NET 10 Desktop Runtime", Category = "Runtimes", Description = "Modern .NET desktop runtime." }
    };

    public static IReadOnlyList<WingetPackageItem> LoadCatalog()
    {
        string customPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            CoreConstants.ProductName, "winget_catalog.json");
        try
        {
            if (File.Exists(customPath))
            {
                var parsed = JsonSerializer.Deserialize<List<WingetPackageItem>>(File.ReadAllText(customPath));
                if (parsed is { Count: > 0 }) return parsed;
            }
        }
        catch { }

        return DefaultCatalog.ConvertAll(p => new WingetPackageItem
        {
            Id = p.Id,
            Name = p.Name,
            Category = p.Category,
            Description = p.Description
        });
    }

    public static async Task<bool> IsWingetAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await RunWingetAsync(new[] { "--version" }, ct);
            return res.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<HashSet<string>> GetInstalledPackageIdsAsync(IEnumerable<string> candidateIds, CancellationToken ct = default)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var res = await RunWingetAsync(new[] { "list", "--source", "winget", "--accept-source-agreements" }, ct);
        if (res.ExitCode != 0 && string.IsNullOrWhiteSpace(res.StdOut))
            return installed;

        foreach (string id in candidateIds)
        {
            if (res.StdOut.Contains(id, StringComparison.OrdinalIgnoreCase))
                installed.Add(id);
        }
        return installed;
    }

    public static async Task<(bool Ok, string Message)> InstallPackageAsync(string packageId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(packageId) || packageId.IndexOfAny(new[] { ' ', '&', '|', ';', '"', '\'' }) >= 0)
            return (false, $"Invalid package ID '{packageId}'.");

        var res = await RunWingetAsync(new[]
        {
            "install",
            "--id", packageId,
            "--exact",
            "--source", "winget",
            "--silent",
            "--accept-package-agreements",
            "--accept-source-agreements"
        }, ct);

        return res.ExitCode switch
        {
            0 => (true, $"Installed '{packageId}'."),
            -1978335189 => (true, $"'{packageId}' is already installed and up to date."),
            -1978335212 => (false, $"Package '{packageId}' not found in winget source."),
            _ => (false, $"winget exited with code {res.ExitCode} ({(string.IsNullOrWhiteSpace(res.StdErr) ? res.StdOut : res.StdErr).Trim()}).")
        };
    }

    public static async Task<(bool Ok, string Message)> UpgradeAllAsync(CancellationToken ct = default)
    {
        var res = await RunWingetAsync(new[]
        {
            "upgrade",
            "--all",
            "--source", "winget",
            "--silent",
            "--accept-package-agreements",
            "--accept-source-agreements"
        }, ct);
        return res.ExitCode == 0
            ? (true, "All winget packages upgraded.")
            : (false, $"winget upgrade exited with code {res.ExitCode}.");
    }

    private static async Task<ToolRunResult> RunWingetAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string wingetPath = Path.Combine(localAppData, @"Microsoft\WindowsApps\winget.exe");
        if (!File.Exists(wingetPath))
            wingetPath = "winget.exe";

        var psi = new ProcessStartInfo
        {
            FileName = wingetPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();
        using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } });
        var outT = proc.StandardOutput.ReadToEndAsync(ct);
        var errT = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return new ToolRunResult(proc.ExitCode, await outT, await errT);
    }
}
