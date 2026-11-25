using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace JamesOptimizer
{
    public static class OptimizationReportGenerator
    {
        public static async Task<string> GenerateAndOpenReportAsync()
        {
            return await Task.Run(() =>
            {
                string reportPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    $"JamesOptimizer_Report_{DateTime.Now:yyyyMMdd_HHmmss}.html");

                // Gather System Specs
                string osName = "Windows 11 Pro 64-bit";
                string cpuName = "Unknown Processor";
                string totalRamStr = "16.0 GB";
                string driveSummaryHtml = "";

                try
                {
                    using var osSearcher = new ManagementObjectSearcher("SELECT Caption, OSArchitecture, Version FROM Win32_OperatingSystem");
                    using var osResults = osSearcher.Get();
                    foreach (var o in osResults)
                    using (o)
                    {
                        osName = $"{o["Caption"]} ({o["OSArchitecture"]}) - Build {o["Version"]}".Replace("Microsoft", "").Trim();
                        break;
                    }
                }
                catch { }

                try
                {
                    using var cpuSearcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
                    using var cpuResults = cpuSearcher.Get();
                    foreach (var c in cpuResults)
                    using (c)
                    {
                        cpuName = $"{c["Name"]} ({c["NumberOfCores"]} Cores, {c["NumberOfLogicalProcessors"]} Threads)";
                        break;
                    }
                }
                catch { }

                try
                {
                    using var csSearcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                    using var csResults = csSearcher.Get();
                    foreach (var cs in csResults)
                    using (cs)
                    {
                        if (ulong.TryParse(cs["TotalPhysicalMemory"]?.ToString(), out ulong bytes))
                        {
                            totalRamStr = $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
                        }
                        break;
                    }
                }
                catch { }

                try
                {
                    var sbDrives = new StringBuilder();
                    foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed))
                    {
                        long total = d.TotalSize;
                        long free = d.TotalFreeSpace;
                        long used = total - free;
                        double pct = total > 0 ? (used / (double)total) * 100.0 : 0;
                        string barColor = pct > 85 ? "#DC2626" : (pct > 70 ? "#F59E0B" : "#00E5FF");

                        sbDrives.Append($@"
                        <div class='drive-card'>
                            <div class='drive-header'>
                                <span class='drive-letter'>{d.Name} ({d.VolumeLabel})</span>
                                <span class='drive-pct'>{pct:F0}% Used</span>
                            </div>
                            <div class='progress-track'>
                                <div class='progress-fill' style='width: {pct:F0}%; background: {barColor};'></div>
                            </div>
                            <div class='drive-footer'>
                                <span>Free: {free / (1024.0 * 1024.0 * 1024.0):F1} GB</span>
                                <span>Total: {total / (1024.0 * 1024.0 * 1024.0):F1} GB</span>
                            </div>
                        </div>");
                    }
                    driveSummaryHtml = sbDrives.ToString();
                }
                catch { }

                var statusRows = new List<(string Name, string Detail, bool Applied)>();
                int liveScore = 60;

                try
                {
                    var schemeRes = JamesOptimizer.Core.NativeSystemHelper.RunSystem32ToolSync("powercfg.exe", new[] { "/getactivescheme" }, 2000);
                    string scheme = schemeRes.StdOut;
                    bool highPerf = scheme.Contains("e9a42b02", StringComparison.OrdinalIgnoreCase)
                                 || scheme.Contains("8c5e7fda", StringComparison.OrdinalIgnoreCase)
                                 || scheme.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)
                                 || scheme.Contains("High performance", StringComparison.OrdinalIgnoreCase);
                    if (highPerf) liveScore += 12;
                    statusRows.Add(("Power Plan", highPerf ? "High / Ultimate Performance scheme active." : "Running standard Balanced or Power Saver scheme.", highPerf));
                }
                catch { }

                try
                {
                    int? telemetry = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", null) as int?;
                    bool blocked = telemetry == 0;
                    if (blocked) liveScore += 10;
                    statusRows.Add(("Telemetry Policy", blocked ? "AllowTelemetry=0 diagnostic suppression active." : "Default Windows telemetry policy active.", blocked));
                }
                catch { }

                try
                {
                    int? netThrottle = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", null) as int?;
                    int? sysResp = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", null) as int?;
                    bool tuned = netThrottle == unchecked((int)0xFFFFFFFF) || sysResp == 0;
                    if (tuned) liveScore += 10;
                    statusRows.Add(("Network & MMCSS Profile", tuned ? "Network throttling disabled and multimedia responsiveness tuned." : "Stock MMCSS scheduling parameters active.", tuned));
                }
                catch { }

                try
                {
                    var sysDrive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name.StartsWith(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:", StringComparison.OrdinalIgnoreCase));
                    if (sysDrive != null && sysDrive.TotalSize > 0)
                    {
                        double freeRatio = sysDrive.TotalFreeSpace / (double)sysDrive.TotalSize;
                        bool healthyDisk = freeRatio >= 0.15;
                        if (healthyDisk) liveScore += 8;
                        statusRows.Add(("System Drive Headroom", $"{sysDrive.Name.TrimEnd('\\')} has {freeRatio * 100:F0}% free space.", healthyDisk));
                    }
                }
                catch { }

                liveScore = Math.Clamp(liveScore, 0, 100);
                var statusHtml = new StringBuilder();
                foreach (var row in statusRows)
                {
                    string badge = row.Applied ? "✓ APPLIED" : "○ DEFAULT";
                    string badgeStyle = row.Applied ? "" : " style='background:rgba(148,163,184,0.15);color:#94A3B8;'";
                    statusHtml.Append($"<li class='tweak-item'><span class='tweak-badge'{badgeStyle}>{badge}</span><span><strong>{WebUtility.HtmlEncode(row.Name)}</strong> — {WebUtility.HtmlEncode(row.Detail)}</span></li>\n");
                }

                string benchHtml = "";
                try
                {
                    var history = OptimizationEngine.GetBenchmarkHistory();
                    if (history.Count > 0)
                    {
                        var first = history[0];
                        var latest = history[^1];
                        int delta = latest.TotalScore - first.TotalScore;
                        string deltaSign = delta >= 0 ? $"+{delta}" : delta.ToString();
                        benchHtml = $@"
        <div class='section-title'>Benchmark History &amp; Delta</div>
        <div class='card' style='margin-bottom:24px;'>
            <div class='spec-row'><span class='spec-label'>Baseline ({first.Timestamp:yyyy-MM-dd} · {WebUtility.HtmlEncode(first.PowerPlanName)})</span><span class='spec-val'>{first.TotalScore} / 100 (CPU {first.CpuScore}, RAM {first.RamScore}, Disk {first.DiskScore})</span></div>
            <div class='spec-row'><span class='spec-label'>Latest ({latest.Timestamp:yyyy-MM-dd} · {WebUtility.HtmlEncode(latest.PowerPlanName)})</span><span class='spec-val'>{latest.TotalScore} / 100 ({deltaSign} pts)</span></div>
        </div>";
                    }
                }
                catch { }

                string html = $@"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <title>JamesOptimizer V3 · System Optimization Report</title>
    <style>
        :root {{
            --bg: #0B0E14;
            --surface1: #121622;
            --surface2: #181E2E;
            --border: #222B42;
            --accent-cyan: #00E5FF;
            --accent-purple: #8A67FF;
            --accent-green: #00FF9D;
            --text-primary: #F0F4FF;
            --text-secondary: #94A3B8;
            --text-muted: #64748B;
        }}
        * {{ margin: 0; padding: 0; box-sizing: border-box; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }}
        body {{
            background: var(--bg);
            color: var(--text-primary);
            padding: 40px 20px;
            display: flex;
            justify-content: center;
        }}
        .container {{
            max-width: 900px;
            width: 100%;
        }}
        .header {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            border-bottom: 1px solid var(--border);
            padding-bottom: 24px;
            margin-bottom: 32px;
        }}
        .brand {{
            display: flex;
            align-items: center;
            gap: 14px;
        }}
        .brand-badge {{
            background: linear-gradient(135deg, var(--accent-cyan), var(--accent-purple));
            color: #000;
            font-weight: 900;
            font-size: 14px;
            padding: 6px 14px;
            border-radius: 8px;
            letter-spacing: 1px;
        }}
        .brand-title {{
            font-size: 24px;
            font-weight: 800;
            letter-spacing: 0.5px;
        }}
        .brand-title span {{
            background: linear-gradient(90deg, var(--accent-cyan), var(--accent-purple));
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
        }}
        .timestamp {{
            font-size: 13px;
            color: var(--text-muted);
            text-align: right;
        }}
        .score-hero {{
            background: linear-gradient(145deg, #131A2B, #0E1320);
            border: 1px solid var(--border);
            border-radius: 16px;
            padding: 28px;
            display: flex;
            align-items: center;
            justify-content: space-between;
            margin-bottom: 30px;
            box-shadow: 0 10px 30px rgba(0, 229, 255, 0.05);
        }}
        .score-details h2 {{
            font-size: 20px;
            margin-bottom: 6px;
            color: var(--text-primary);
        }}
        .score-details p {{
            font-size: 13px;
            color: var(--text-secondary);
            max-width: 500px;
        }}
        .score-circle {{
            width: 100px;
            height: 100px;
            border-radius: 50%;
            background: radial-gradient(circle, #1A2640 0%, #0E1424 100%);
            border: 4px solid var(--accent-cyan);
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            box-shadow: 0 0 20px rgba(0, 229, 255, 0.4);
        }}
        .score-num {{
            font-size: 32px;
            font-weight: 900;
            color: var(--accent-cyan);
        }}
        .score-max {{
            font-size: 11px;
            color: var(--text-muted);
        }}
        .section-title {{
            font-size: 15px;
            font-weight: 700;
            color: var(--accent-cyan);
            letter-spacing: 1.5px;
            margin-bottom: 16px;
            text-transform: uppercase;
        }}
        .grid-2 {{
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 20px;
            margin-bottom: 30px;
        }}
        .card {{
            background: var(--surface1);
            border: 1px solid var(--border);
            border-radius: 12px;
            padding: 20px;
        }}
        .spec-row {{
            display: flex;
            justify-content: space-between;
            padding: 8px 0;
            border-bottom: 1px solid rgba(255, 255, 255, 0.04);
            font-size: 13px;
        }}
        .spec-label {{ color: var(--text-muted); }}
        .spec-val {{ color: var(--text-primary); font-weight: 600; text-align: right; }}
        .drive-card {{
            margin-bottom: 14px;
        }}
        .drive-header {{
            display: flex;
            justify-content: space-between;
            font-size: 13px;
            font-weight: 600;
            margin-bottom: 6px;
        }}
        .progress-track {{
            width: 100%;
            height: 6px;
            background: var(--surface2);
            border-radius: 3px;
            overflow: hidden;
            margin-bottom: 4px;
        }}
        .progress-fill {{
            height: 100%;
            border-radius: 3px;
        }}
        .drive-footer {{
            display: flex;
            justify-content: space-between;
            font-size: 11px;
            color: var(--text-muted);
        }}
        .tweak-list {{
            list-style: none;
        }}
        .tweak-item {{
            display: flex;
            align-items: center;
            gap: 10px;
            padding: 10px 0;
            border-bottom: 1px solid rgba(255, 255, 255, 0.04);
            font-size: 13px;
        }}
        .tweak-badge {{
            background: rgba(0, 255, 157, 0.15);
            color: var(--accent-green);
            padding: 2px 8px;
            border-radius: 6px;
            font-size: 11px;
            font-weight: 700;
        }}
        .footer {{
            margin-top: 40px;
            border-top: 1px solid var(--border);
            padding-top: 20px;
            text-align: center;
            font-size: 12px;
            color: var(--text-muted);
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <div class='brand'>
                <div class='brand-badge'>PRO v3.2</div>
                <div class='brand-title'>JAMES <span>OPTIMIZER</span></div>
            </div>
            <div class='timestamp'>
                <div>Optimization Report</div>
                <div>{DateTime.Now:MMMM dd, yyyy · HH:mm:ss}</div>
            </div>
        </div>

        <div class='score-hero'>
            <div class='score-details'>
                <h2>Live System Optimization Audit</h2>
                <p>Snapshot compiled from active power policy, Windows telemetry policy, MMCSS responsiveness keys, memory, and volume headroom.</p>
            </div>
            <div class='score-circle'>
                <span class='score-num'>{liveScore}</span>
                <span class='score-max'>/ 100</span>
            </div>
        </div>

        <div class='section-title'>Hardware &amp; Operating Environment</div>
        <div class='grid-2'>
            <div class='card'>
                <div class='spec-row'><span class='spec-label'>Operating System</span><span class='spec-val'>{WebUtility.HtmlEncode(osName)}</span></div>
                <div class='spec-row'><span class='spec-label'>Processor</span><span class='spec-val'>{WebUtility.HtmlEncode(cpuName)}</span></div>
                <div class='spec-row'><span class='spec-label'>Physical Memory</span><span class='spec-val'>{WebUtility.HtmlEncode(totalRamStr)}</span></div>
                <div class='spec-row'><span class='spec-label'>Active User</span><span class='spec-val'>{WebUtility.HtmlEncode(Environment.UserName)}</span></div>
                <div class='spec-row'><span class='spec-label'>Machine Host</span><span class='spec-val'>{WebUtility.HtmlEncode(Environment.MachineName)}</span></div>
            </div>
            <div class='card'>
                <div style='font-size: 12px; font-weight: 700; color: var(--text-secondary); margin-bottom: 12px;'>STORAGE VOLUMES</div>
                {driveSummaryHtml}
            </div>
        </div>

        <div class='section-title'>Verified System Configuration Checks</div>
        <div class='card'>
            <ul class='tweak-list'>
                {statusHtml}
            </ul>
        </div>
        {benchHtml}

        <div class='footer'>
            Generated automatically by <strong>JamesOptimizer V3</strong> · Designed for ultra performance and zero compromises.
        </div>
    </div>
</body>
</html>";

                File.WriteAllText(reportPath, html, Encoding.UTF8);

                // Open in default browser
                try
                {
                    Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
                }
                catch { }

                return reportPath;
            });
        }
    }
}
