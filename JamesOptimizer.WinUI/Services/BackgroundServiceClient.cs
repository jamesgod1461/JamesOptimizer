using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JamesOptimizer.WinUI.Services
{
    public class BackgroundServiceClient
    {
        private const string PipeName = "JamesOptimizerPipe";

        public static async Task<string> SendCommandAsync(string command, int timeoutMs = 1500)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
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

        public static async Task<bool> PingAsync()
        {
            return await IsServiceOnlineAsync();
        }

        public static async Task<bool> IsServiceOnlineAsync()
        {
            var res = await SendCommandAsync("PING", 800);
            return res.StartsWith("PONG", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<string> GetStatusAsync()
        {
            return await SendCommandAsync("STATUS", 1000);
        }

        public static async Task<string> RequestOptimizationAsync()
        {
            return await SendCommandAsync("OPTIMIZE", 10000);
        }

        public static async Task<bool> SetAutoTimerAsync(int minutes)
        {
            var res = await SendCommandAsync($"SET_TIMER:{minutes}", 2000);
            return res.StartsWith("SUCCESS", StringComparison.OrdinalIgnoreCase);
        }
    }
}
