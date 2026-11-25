using System;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JamesOptimizer.Service;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class Program
{
    public static void Main(string[] args)
    {
        SharedLogger.ConfigureForService();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                SharedLogger.Error("SERVICE_FATAL", $"Unhandled service exception (terminating={e.IsTerminating})", ex);
            else
                SharedLogger.Error("SERVICE_FATAL", $"Unhandled non-exception object (terminating={e.IsTerminating}): {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            SharedLogger.Error("SERVICE_TASK", "Unobserved background task exception", e.Exception);
            e.SetObserved();
        };

        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = CoreConstants.ServiceName;
        });

        builder.Services.AddHostedService<Worker>();

        var host = builder.Build();
        host.Run();
    }
}
