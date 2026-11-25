using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JamesOptimizer.Core;

public sealed record DnsPreset(string Name, string PrimaryV4, string SecondaryV4, string PrimaryV6, string SecondaryV6, string DohTemplate);

public sealed class DnsBenchmarkResult
{
    public string ProviderName { get; set; } = "";
    public string ServerIp { get; set; } = "";
    public double MedianMs { get; set; }
    public double P95Ms { get; set; }
    public double JitterMs { get; set; }
    public double LossPercent { get; set; }
    public bool Succeeded { get; set; }

    public DnsBenchmarkResult() { }
    public DnsBenchmarkResult(string providerName, string serverIp, double medianMs, double p95Ms, double jitterMs, double lossPercent, bool succeeded)
    {
        ProviderName = providerName;
        ServerIp = serverIp;
        MedianMs = medianMs;
        P95Ms = p95Ms;
        JitterMs = jitterMs;
        LossPercent = lossPercent;
        Succeeded = succeeded;
    }
}

public sealed class SocketProcessEntry
{
    public string Protocol { get; set; } = "";
    public string LocalEndpoint { get; set; } = "";
    public string RemoteEndpoint { get; set; } = "";
    public string State { get; set; } = "";
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public bool IsCritical { get; set; }

    public SocketProcessEntry() { }
    public SocketProcessEntry(string protocol, string localEndpoint, string remoteEndpoint, string state, int processId, string processName, string executablePath, bool isCritical)
    {
        Protocol = protocol;
        LocalEndpoint = localEndpoint;
        RemoteEndpoint = remoteEndpoint;
        State = state;
        ProcessId = processId;
        ProcessName = processName;
        ExecutablePath = executablePath;
        IsCritical = isCritical;
    }
}

[SupportedOSPlatform("windows")]
public static class NetworkDiagnosticsService
{
    public static readonly IReadOnlyList<DnsPreset> Presets = new[]
    {
        new DnsPreset("Cloudflare", "1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001", "https://cloudflare-dns.com/dns-query"),
        new DnsPreset("Google", "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844", "https://dns.google/dns-query"),
        new DnsPreset("Quad9", "9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9", "https://dns.quad9.net/dns-query"),
        new DnsPreset("AdGuard", "94.140.14.14", "94.140.15.15", "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff", "https://dns.adguard-dns.com/dns-query"),
        new DnsPreset("OpenDNS", "208.67.222.222", "208.67.220.220", "2620:119:35::35", "2620:119:53::53", "https://doh.opendns.com/dns-query"),
        new DnsPreset("NextDNS", "45.90.28.190", "45.90.30.190", "2a07:a8c0::", "2a07:a8c1::", "https://dns.nextdns.io")
    };

    public static List<NetworkInterface> GetActivePhysicalAdapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                            && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                            && !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                            && !n.Description.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
                .ThenByDescending(n => n.Speed)
                .ToList();
        }
        catch
        {
            return new List<NetworkInterface>();
        }
    }

    public static async Task<List<DnsBenchmarkResult>> BenchmarkDnsServersAsync(
        IEnumerable<(string Name, string Ip)> servers,
        int repetitions = 5,
        CancellationToken ct = default)
    {
        var results = new List<DnsBenchmarkResult>();
        string[] probeDomains = { "cloudflare.com", "microsoft.com", "github.com" };

        foreach (var (name, ipStr) in servers)
        {
            ct.ThrowIfCancellationRequested();
            if (!IPAddress.TryParse(ipStr, out var ip))
            {
                results.Add(new DnsBenchmarkResult(name, ipStr, 0, 0, 0, 100, false));
                continue;
            }

            var samples = new List<double>();
            int totalAttempts = 0;
            int failures = 0;

            for (int r = 0; r < repetitions; r++)
            {
                ct.ThrowIfCancellationRequested();
                string domain = r == 0 ? probeDomains[0] : $"{Guid.NewGuid().ToString("N")[..8]}.{probeDomains[r % probeDomains.Length]}";
                totalAttempts++;
                double? ms = await QueryUdpDnsAsync(ip, domain, 1200, ct);
                if (ms.HasValue)
                    samples.Add(ms.Value);
                else
                    failures++;
            }

            if (samples.Count == 0)
            {
                results.Add(new DnsBenchmarkResult(name, ipStr, 0, 0, 0, 100.0, false));
                continue;
            }

            samples.Sort();
            double median = samples[samples.Count / 2];
            int p95Idx = Math.Clamp((int)Math.Ceiling(samples.Count * 0.95) - 1, 0, samples.Count - 1);
            double p95 = samples[p95Idx];
            double jitter = samples.Count > 1
                ? samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).Average()
                : 0;
            double loss = (failures / (double)totalAttempts) * 100.0;

            results.Add(new DnsBenchmarkResult(name, ipStr, Math.Round(median, 1), Math.Round(p95, 1), Math.Round(jitter, 1), Math.Round(loss, 0), true));
        }

        return results.OrderByDescending(r => r.Succeeded).ThenBy(r => r.MedianMs).ToList();
    }

    private static async Task<double?> QueryUdpDnsAsync(IPAddress dnsIp, string domain, int timeoutMs, CancellationToken ct)
    {
        try
        {
            byte[] packet = BuildDnsQueryPacket(domain);
            using var udp = new UdpClient(dnsIp.AddressFamily);
            udp.Client.ReceiveTimeout = timeoutMs;
            var endpoint = new IPEndPoint(dnsIp, 53);

            var sw = Stopwatch.StartNew();
            await udp.SendAsync(packet, packet.Length, endpoint);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);
            var recv = await udp.ReceiveAsync(timeoutCts.Token);
            sw.Stop();
            return recv.Buffer.Length >= 12 ? sw.Elapsed.TotalMilliseconds : null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] BuildDnsQueryPacket(string domain)
    {
        var buf = new List<byte>
        {
            0xAB, 0xCD, // TxID
            0x01, 0x00, // Standard recursive query
            0x00, 0x01, // 1 question
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        foreach (string label in domain.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            byte[] b = Encoding.ASCII.GetBytes(label);
            buf.Add((byte)b.Length);
            buf.AddRange(b);
        }
        buf.Add(0x00);
        buf.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 }); // Type A, Class IN
        return buf.ToArray();
    }

    public static async Task<bool> EnableWindows11DohAsync(string serverIp, string dohTemplate, CancellationToken ct = default)
    {
        if (OsEnvironment.Detect().BuildNumber < 22000 || string.IsNullOrWhiteSpace(dohTemplate))
            return false;

        var res = await NativeSystemHelper.RunSystem32ToolAsync(
            "netsh.exe",
            new[] { "dns", "add", "encryption", $"server={serverIp}", $"dohtemplate={dohTemplate}", "autoupgrade=yes", "udpfallback=no" },
            ct);
        return res.ExitCode == 0;
    }

    public static List<SocketProcessEntry> GetActiveTcpConnectionsWithPid()
    {
        var list = new List<SocketProcessEntry>();
        var procCache = new Dictionary<int, (string Name, string Path)>();

        int bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (bufferSize <= 0) return list;

        IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return list;

            int rowCount = Marshal.ReadInt32(tcpTablePtr);
            IntPtr rowPtr = IntPtr.Add(tcpTablePtr, 4);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < rowCount && i < 300; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rowPtr = IntPtr.Add(rowPtr, rowSize);

                int pid = (int)row.owningPid;
                if (!procCache.TryGetValue(pid, out var pInfo))
                {
                    string pname = $"PID {pid}";
                    string ppath = "";
                    try
                    {
                        using var p = Process.GetProcessById(pid);
                        pname = p.ProcessName;
                        try { ppath = p.MainModule?.FileName ?? ""; } catch { }
                    }
                    catch { }
                    pInfo = (pname, ppath);
                    procCache[pid] = pInfo;
                }

                string localIp = new IPAddress(row.localAddr).ToString();
                int localPort = (ushort)IPAddress.NetworkToHostOrder((short)row.localPort);
                string remoteIp = new IPAddress(row.remoteAddr).ToString();
                int remotePort = (ushort)IPAddress.NetworkToHostOrder((short)row.remotePort);
                string state = ((TcpState)row.state).ToString();

                list.Add(new SocketProcessEntry(
                    "TCP",
                    $"{localIp}:{localPort}",
                    $"{remoteIp}:{remotePort}",
                    state,
                    pid,
                    pInfo.Name,
                    pInfo.Path,
                    CoreConstants.IsProtectedProcess(pInfo.Name)));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tcpTablePtr);
        }

        return list.OrderBy(c => c.State == "Listen" ? 1 : 0).ThenBy(c => c.ProcessName).ToList();
    }

    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort;
        public uint remoteAddr;
        public uint remotePort;
        public uint owningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, [MarshalAs(UnmanagedType.Bool)] bool bOrder, int ulAf, int TableClass, uint Reserved);
}
