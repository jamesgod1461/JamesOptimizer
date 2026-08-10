using JamesOptimizer.Service;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace JamesOptimizer.Service
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            
            builder.Services.AddWindowsService(options =>
            {
                options.ServiceName = "James Optimizer Background Service";
            });

            builder.Services.AddHostedService<Worker>();

            var host = builder.Build();
            host.Run();
        }
    }
}
