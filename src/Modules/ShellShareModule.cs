using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Win32;

namespace PCManagerCompatCli.Modules;

[SupportedOSPlatform("windows")]
internal sealed class ShellShareModule
{
    private const uint WmCopyData = 0x004A;

    private static readonly string[] WindowClassNames =
    {
        "XiaomiPCManager",
        "MiPcContinuity"
    };

    private static readonly string[] PcManagerRegKeys =
    {
        "{504d69c0-cb52-48df-b5b5-7161829fabc8}",
        "{1bca9901-05c3-4d01-8ad4-78da2eac9b3f}"
    };

    public void Execute(string[] rawPaths)
    {
        var paths = NormalizeExistingPaths(rawPaths);
        if (paths.Count == 0)
        {
            throw new ArgumentException("shell-share 需要至少一个有效文件或目录路径");
        }

        var payload = string.Join("|", paths);
        var window = FindShareWindow();

        if (window == IntPtr.Zero)
        {
            LaunchPcManager();
            window = WaitForShareWindow(TimeSpan.FromSeconds(12));
        }

        if (window == IntPtr.Zero)
        {
            throw new InvalidOperationException("未检测到小米电脑管家互传窗口，请先启动小米电脑管家");
        }

        if (!SendFiles(window, payload, TimeSpan.FromSeconds(8)))
        {
            throw new InvalidOperationException("发送文件到小米互传失败");
        }

        Console.WriteLine($"[shell-share] 已发送 {paths.Count} 个项目");
    }

    private static List<string> NormalizeExistingPaths(IEnumerable<string> rawPaths)
    {
        var results = new List<string>();

        foreach (var raw in rawPaths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var token = raw.Trim();
            if (token == "--")
            {
                continue;
            }

            var normalized = token.Trim('"');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(normalized);
            }
            catch
            {
                continue;
            }

            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                results.Add(fullPath);
            }
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void LaunchPcManager()
    {
        var executable = ResolvePcManagerExecutablePath();
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new FileNotFoundException("未找到 XiaomiPcManager.exe 或 MiPcContinuity.exe");
        }

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true
        };

        Process.Start(psi);
    }

    private static IntPtr WaitForShareWindow(TimeSpan timeout)
    {
        var start = Stopwatch.StartNew();
        while (start.Elapsed < timeout)
        {
            var hwnd = FindShareWindow();
            if (hwnd != IntPtr.Zero)
            {
                return hwnd;
            }

            Thread.Sleep(350);
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindShareWindow()
    {
        foreach (var className in WindowClassNames)
        {
            var hwnd = FindWindowW(className, null);
            if (hwnd != IntPtr.Zero)
            {
                return hwnd;
            }
        }

        return IntPtr.Zero;
    }

    private static bool SendFiles(IntPtr hwnd, string payload, TimeSpan timeout)
    {
        var payloadPtr = Marshal.StringToHGlobalUni(payload);
        var cds = new CopyDataStruct
        {
            dwData = IntPtr.Zero,
            cbData = payload.Length * 2,
            lpData = payloadPtr
        };

        var cdsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CopyDataStruct>());

        try
        {
            Marshal.StructureToPtr(cds, cdsPtr, false);

            var timeoutMs = (uint)Math.Clamp((int)timeout.TotalMilliseconds, 0, 15000);
            var result = SendMessageTimeoutW(
                hwnd,
                WmCopyData,
                new IntPtr(1),
                cdsPtr,
                0,
                timeoutMs,
                out _);

            return result != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(cdsPtr);
            Marshal.FreeHGlobal(payloadPtr);
        }
    }

    private static string? ResolvePcManagerExecutablePath()
    {
        var installDir = TryFindPcManagerInstallDir();
        if (string.IsNullOrWhiteSpace(installDir))
        {
            installDir = TryFindPcManagerInstallDirByVersionScan();
        }

        if (string.IsNullOrWhiteSpace(installDir))
        {
            return null;
        }

        var xiaomiExe = Path.Combine(installDir, "XiaomiPcManager.exe");
        if (File.Exists(xiaomiExe))
        {
            return xiaomiExe;
        }

        var continuityExe = Path.Combine(installDir, "MiPcContinuity.exe");
        return File.Exists(continuityExe) ? continuityExe : null;
    }

    private static string? TryFindPcManagerInstallDir()
    {
        foreach (var clsid in PcManagerRegKeys)
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\InprocServer32", writable: false);
            var value = key?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var folder = Path.GetDirectoryName(value);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                return folder;
            }
        }

        return null;
    }

    private static string? TryFindPcManagerInstallDirByVersionScan()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MI", "XiaomiPCManager"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MI", "XiaomiPCManager")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        var candidates = new List<(string Dir, Version Version, DateTime LastWriteUtc)>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var versionDir in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                var xiaomiExe = Path.Combine(versionDir, "XiaomiPcManager.exe");
                var continuityExe = Path.Combine(versionDir, "MiPcContinuity.exe");
                if (!File.Exists(xiaomiExe) && !File.Exists(continuityExe))
                {
                    continue;
                }

                var versionName = Path.GetFileName(versionDir);
                var version = ParseVersionOrZero(versionName);
                var timestamp = File.GetLastWriteTimeUtc(File.Exists(xiaomiExe) ? xiaomiExe : continuityExe);

                candidates.Add((versionDir, version, timestamp));
            }
        }

        return candidates
            .OrderByDescending(item => item.Version)
            .ThenByDescending(item => item.LastWriteUtc)
            .Select(item => item.Dir)
            .FirstOrDefault();
    }

    private static Version ParseVersionOrZero(string? raw)
    {
        return Version.TryParse(raw, out var parsed)
            ? parsed
            : new Version(0, 0);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowW(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }
}
