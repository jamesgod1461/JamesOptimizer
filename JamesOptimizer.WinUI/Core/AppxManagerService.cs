using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using JamesOptimizer.Core;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace JamesOptimizer.WinUI.Core;

public enum AppxSafetyTier
{
    SafeToRemove,
    XboxAndGaming,
    RecommendedKeep,
    NeverRemove
}

public sealed class AppxPackageEntry
{
    public string Name { get; set; } = "";
    public string PackageFullName { get; set; } = "";
    public string PackageFamilyName { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public AppxSafetyTier Tier { get; set; }
    public string TierLabel => Tier switch
    {
        AppxSafetyTier.SafeToRemove => "SAFE TO REMOVE",
        AppxSafetyTier.XboxAndGaming => "XBOX / GAMING",
        AppxSafetyTier.RecommendedKeep => "KEEP RECOMMENDED",
        AppxSafetyTier.NeverRemove => "PROTECTED SYSTEM",
        _ => "APPX"
    };
    public bool CanRemove => Tier != AppxSafetyTier.NeverRemove && !IsDependedOn;
    public bool IsDependedOn { get; set; }
    public bool IsSelected { get; set; }
}

/// <summary>
/// Native C# UWP/Appx package enumerator and remover via Windows.Management.Deployment.PackageManager.
/// Replaces external PowerShell debloater scripts cleanly.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class AppxManagerService
{
    private static readonly HashSet<string> ProtectedPackagePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.WindowsStore",
        "Microsoft.DesktopAppInstaller",
        "Microsoft.VCLibs",
        "Microsoft.UI.Xaml",
        "Microsoft.NET.Native",
        "Microsoft.WindowsAppRuntime",
        "Microsoft.WindowsTerminal",
        "Microsoft.WindowsNotepad",
        "Microsoft.SecHealthUI",
        "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost",
        "MicrosoftWindows.Client",
        "Windows.immersivecontrolpanel"
    };

    private static readonly HashSet<string> KnownSafeBloatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.BingNews",
        "Microsoft.BingWeather",
        "Microsoft.BingSearch",
        "Microsoft.GetHelp",
        "Microsoft.Getstarted",
        "Microsoft.Messaging",
        "Microsoft.Microsoft3DViewer",
        "Microsoft.MicrosoftOfficeHub",
        "Microsoft.MicrosoftSolitaireCollection",
        "Microsoft.MixedReality.Portal",
        "Microsoft.News",
        "Microsoft.Office.OneNote",
        "Microsoft.OneConnect",
        "Microsoft.People",
        "Microsoft.Print3D",
        "Microsoft.SkypeApp",
        "Microsoft.Todos",
        "Microsoft.Whiteboard",
        "Microsoft.WindowsFeedbackHub",
        "Microsoft.WindowsMaps",
        "Microsoft.YourPhone",
        "Microsoft.ZuneMusic",
        "Microsoft.ZuneVideo",
        "Microsoft.549981C3F5F10",
        "Clipchamp.Clipchamp",
        "Microsoft.Copilot"
    };

    public static async Task<List<AppxPackageEntry>> GetInstalledPackagesAsync()
    {
        return await Task.Run(() =>
        {
            var pm = new PackageManager();
            var allPackages = pm.FindPackagesForUser("").ToList();

            var dependedOnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pkg in allPackages)
            {
                try
                {
                    foreach (var dep in pkg.Dependencies)
                    {
                        dependedOnNames.Add(dep.Id.Name);
                    }
                }
                catch { }
            }

            var list = new List<AppxPackageEntry>();
            foreach (var pkg in allPackages)
            {
                try
                {
                    if (pkg.IsFramework || pkg.IsResourcePackage || pkg.SignatureKind == PackageSignatureKind.System)
                        continue;

                    string name = pkg.Id.Name;
                    bool isDepended = dependedOnNames.Contains(name);
                    var tier = ClassifyPackage(name, isDepended);

                    list.Add(new AppxPackageEntry
                    {
                        Name = string.IsNullOrWhiteSpace(pkg.DisplayName) ? name : pkg.DisplayName,
                        PackageFullName = pkg.Id.FullName,
                        PackageFamilyName = pkg.Id.FamilyName,
                        Publisher = pkg.PublisherDisplayName ?? pkg.Id.Publisher,
                        Version = $"{pkg.Id.Version.Major}.{pkg.Id.Version.Minor}.{pkg.Id.Version.Build}.{pkg.Id.Version.Revision}",
                        Tier = tier,
                        IsDependedOn = isDepended
                    });
                }
                catch { }
            }

            return list.OrderBy(p => p.Tier).ThenBy(p => p.Name).ToList();
        });
    }

    private static AppxSafetyTier ClassifyPackage(string name, bool isDepended)
    {
        if (isDepended || ProtectedPackagePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return AppxSafetyTier.NeverRemove;
        if (name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) || name.Contains("GamingApp", StringComparison.OrdinalIgnoreCase))
            return AppxSafetyTier.XboxAndGaming;
        if (KnownSafeBloatNames.Contains(name))
            return AppxSafetyTier.SafeToRemove;
        return AppxSafetyTier.RecommendedKeep;
    }

    public static async Task<(bool Ok, string Message)> RemovePackageAsync(AppxPackageEntry entry)
    {
        if (!entry.CanRemove)
            return (false, $"'{entry.Name}' is protected or depended on by another package.");

        try
        {
            var pm = new PackageManager();
            var op = pm.RemovePackageAsync(entry.PackageFullName);
            var res = await op.AsTask();
            if (res.IsRegistered)
                return (false, $"Package still registered: {res.ErrorText}");

            RecordRemovedPackage(entry);
            SharedLogger.Info("APPX", $"Removed UWP package '{entry.PackageFullName}'.");
            return (true, $"Removed '{entry.Name}'.");
        }
        catch (Exception ex)
        {
            SharedLogger.Error("APPX", $"Failed removing '{entry.PackageFullName}'", ex);
            return (false, ex.Message);
        }
    }

    private static void RecordRemovedPackage(AppxPackageEntry entry)
    {
        try
        {
            string logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                CoreConstants.ProductName, "backups", "removed_appx.txt");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
            System.IO.File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {entry.Name} | {entry.PackageFullName} | Family={entry.PackageFamilyName}{Environment.NewLine}");
        }
        catch { }
    }
}
