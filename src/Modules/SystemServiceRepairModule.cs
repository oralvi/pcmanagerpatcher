using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;
using PCManagerCompatCli.Infrastructure;

namespace PCManagerCompatCli.Modules;

internal sealed record SystemServiceRepairOptions(
    bool RepairWindowsServiceStack,
    bool RepairContinuityServices,
    bool InstallWebView2Online,
    bool ForceInstallWebView2,
    string WebView2BootstrapperUrl,
    int ServiceStartTimeoutSeconds);

[SupportedOSPlatform("windows")]
internal sealed class SystemServiceRepairModule
{
    private static readonly string[] PreferredContinuityServices =
    {
        "XiaomiPCManagerService",
        "XiaomiDeviceService",
        "XiaomiContinuityService",
        "MiDeviceService",
        "CrossDeviceService",
        "MiDropService"
    };

    private static readonly string[] ContinuityKeywords =
    {
        "xiaomi",
        "pcmanager",
        "midevice",
        "continuity",
        "crossdevice",
        "midrop"
    };

    private const string WebView2ClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    private static readonly int[] DismSuccessExitCodes = { 0, 3010 };

    public void Execute(SystemServiceRepairOptions options)
    {
        Console.WriteLine("[repair-system] 开始...");

        if (options.RepairWindowsServiceStack)
        {
            Console.WriteLine("[repair-system] 修复 Windows 组件（DISM）...");
            RepairWindowsServiceStack();
        }

        if (options.RepairContinuityServices)
        {
            Console.WriteLine("[repair-system] 修复互联互通服务...");
            RepairContinuityServices(options.ServiceStartTimeoutSeconds);
        }

        if (options.InstallWebView2Online)
        {
            Console.WriteLine("[repair-system] 在线安装/修复 Microsoft Edge WebView2 Runtime...");
            InstallWebView2Online(options.ForceInstallWebView2, options.WebView2BootstrapperUrl);
        }

        Console.WriteLine("[repair-system] 完成");
    }

    private static void RepairWindowsServiceStack()
    {
        RunDismHealthRepair();
    }

    private static void RunDismHealthRepair()
    {
        Console.WriteLine("  运行 DISM 检查/修复组件存储...");
        RunDismCommand(
            new[] { "/Online", "/Cleanup-Image", "/CheckHealth" },
            "CheckHealth");
        RunDismCommand(
            new[] { "/Online", "/Cleanup-Image", "/ScanHealth" },
            "ScanHealth");
        RunDismCommand(
            new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" },
            "RestoreHealth");
    }

    private static void RunDismCommand(string[] args, string label)
    {
        var result = SystemUtil.RunProcess("dism.exe", args, captureOutput: true);
        if (!DismSuccessExitCodes.Contains(result.ExitCode))
        {
            var details = !string.IsNullOrWhiteSpace(result.StdErr)
                ? result.StdErr
                : result.StdOut;

            throw new InvalidOperationException(
                $"DISM {label} 失败，退出码: {result.ExitCode}; {TrimOutput(details)}");
        }

        Console.WriteLine($"  DISM {label}: 完成 (exit={result.ExitCode})");
    }

    private static string TrimOutput(string text, int max = 400)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "<no output>";
        }

        var cleaned = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (cleaned.Length <= max)
        {
            return cleaned;
        }

        return cleaned[..max] + "...";
    }

    private static void RepairContinuityServices(int timeoutSeconds)
    {
        var targets = new HashSet<string>(PreferredContinuityServices, StringComparer.OrdinalIgnoreCase);

        foreach (var discovered in DiscoverContinuityServices())
        {
            targets.Add(discovered);
        }

        if (targets.Count == 0)
        {
            Console.WriteLine("  未发现可修复的互联服务");
            return;
        }

        foreach (var serviceName in targets)
        {
            EnsureServiceRunning(serviceName, timeoutSeconds, ignoreMissing: true);
        }
    }

    private static IEnumerable<string> DiscoverContinuityServices()
    {
        ServiceController[] services;
        try
        {
            services = ServiceController.GetServices();
        }
        catch
        {
            yield break;
        }

        foreach (var svc in services)
        {
            var name = svc.ServiceName ?? string.Empty;
            var displayName = svc.DisplayName ?? string.Empty;

            if (ContinuityKeywords.Any(key =>
                    name.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                    displayName.Contains(key, StringComparison.OrdinalIgnoreCase)))
            {
                yield return name;
            }
        }
    }

    private static void EnsureServiceRunning(string serviceName, int timeoutSeconds, bool ignoreMissing = false)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            _ = sc.Status;

            var configResult = SystemUtil.RunProcess(
                "sc.exe",
                new[] { "config", serviceName, "start=", "auto" });

            if (!configResult.IsSuccess)
            {
                Console.WriteLine($"  {serviceName}: 设置启动类型失败（继续）");
            }

            if (sc.Status == ServiceControllerStatus.Running)
            {
                Console.WriteLine($"  {serviceName}: 已在运行");
                return;
            }

            if (sc.Status == ServiceControllerStatus.StartPending)
            {
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(timeoutSeconds));
                Console.WriteLine($"  {serviceName}: 已运行");
                return;
            }

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(timeoutSeconds));
            Console.WriteLine($"  {serviceName}: 启动成功");
        }
        catch (InvalidOperationException)
        {
            if (!ignoreMissing)
            {
                Console.WriteLine($"  {serviceName}: 服务不存在或不可访问");
            }
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            Console.WriteLine($"  {serviceName}: 启动超时");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {serviceName}: 修复失败 - {ex.Message}");
        }
    }

    private static void InstallWebView2Online(bool forceInstall, string bootstrapperUrl)
    {
        if (!forceInstall && IsWebView2Installed())
        {
            Console.WriteLine("  WebView2 已安装，跳过");
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebView2Setup.exe");

        DownloadFile(bootstrapperUrl, tempPath);

        var result = SystemUtil.RunProcess(
            tempPath,
            new[] { "/silent", "/install" },
            Path.GetDirectoryName(tempPath),
            captureOutput: true);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"WebView2 安装失败，退出码: {result.ExitCode}; {result.StdErr}");
        }

        Console.WriteLine("  WebView2 安装完成");
    }

    private static bool IsWebView2Installed()
    {
        var registryPaths = new[]
        {
            $@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{WebView2ClientGuid}",
            $@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{WebView2ClientGuid}"
        };

        foreach (var path in registryPaths)
        {
            using var key = Registry.LocalMachine.OpenSubKey(path, writable: false);
            var version = key?.GetValue("pv") as string;
            if (!string.IsNullOrWhiteSpace(version))
            {
                return true;
            }
        }

        return false;
    }

    private static void DownloadFile(string url, string outputPath)
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        using var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter()
            .GetResult();

        response.EnsureSuccessStatusCode();

        using var source = response.Content.ReadAsStream();
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(fs);
        fs.Flush();

        // Give antivirus scanner a short moment to finish scanning downloaded executable.
        Thread.Sleep(300);
    }
}
