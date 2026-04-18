using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PCManagerCompatCli.Infrastructure;

namespace PCManagerCompatCli.Modules;

internal sealed record ExtensionSetupOptions(
    string WorkspaceRoot,
    bool ApplyRegistryProfile,
    string ProductModel,
    bool InstallShellExtension,
    bool VerifyShellExtension,
    bool RestartExplorerAfterInstall);

[SupportedOSPlatform("windows")]
internal sealed class ExtensionSetupModule
{
    private const string ShellVerbName = "PCManagerCompat.XiaomiShare";
    private const string ShellVerbTitle = "使用小米互传发送";

    private static readonly string[] PcManagerRegKeys =
    {
        "{504d69c0-cb52-48df-b5b5-7161829fabc8}",
        "{1bca9901-05c3-4d01-8ad4-78da2eac9b3f}"
    };

    public void Execute(ExtensionSetupOptions options)
    {
        _ = Path.GetFullPath(options.WorkspaceRoot);

        if (options.ApplyRegistryProfile)
        {
            Console.WriteLine("[extension-setup] 写入机型扩展注册表...");
            new RegistryProfileService().ApplyModelProfile(options.ProductModel);
        }

        if (options.InstallShellExtension)
        {
            Console.WriteLine("[extension-setup] 安装右键菜单扩展（纯注册表模式，不使用 Appx）...");
            InstallClassicShellExtension(options.VerifyShellExtension, options.RestartExplorerAfterInstall);
        }

        Console.WriteLine("[extension-setup] 完成");
    }

    private static void InstallClassicShellExtension(bool verifyShellExtension, bool restartExplorerAfterInstall)
    {
        var exePath = ResolveCurrentExecutablePath();
        var icon = ResolvePreferredIcon(exePath);

        RegisterClassicVerb(@"*\shell\" + ShellVerbName, BuildSelectionCommand(exePath), icon, supportsMultiSelect: true);
        RegisterClassicVerb(@"Directory\shell\" + ShellVerbName, BuildSelectionCommand(exePath), icon, supportsMultiSelect: true);
        RegisterClassicVerb(@"Directory\Background\shell\" + ShellVerbName, BuildBackgroundCommand(exePath), icon, supportsMultiSelect: false);

        Console.WriteLine($"  已注册右键菜单命令: {exePath}");
        Console.WriteLine("  模式: 纯注册表 + shell-share 子命令（无 Appx 依赖）");
        Console.WriteLine("  Win11 提示：通常显示在“显示更多选项(Shift+F10)”中");

        if (verifyShellExtension)
        {
            VerifyClassicShellExtension(exePath);
        }

        if (restartExplorerAfterInstall)
        {
            TryRestartExplorer();
        }
    }

    private static string ResolveCurrentExecutablePath()
    {
        return Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("无法解析当前程序路径");
    }

    private static string BuildSelectionCommand(string exePath)
    {
        return $"\"{exePath}\" shell-share %*";
    }

    private static string BuildBackgroundCommand(string exePath)
    {
        return $"\"{exePath}\" shell-share \"%V\"";
    }

    private static void RegisterClassicVerb(
        string relativeKeyPath,
        string command,
        string icon,
        bool supportsMultiSelect)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + relativeKeyPath, writable: true)
            ?? throw new InvalidOperationException($"无法创建注册表项: HKCU\\Software\\Classes\\{relativeKeyPath}");

        key.SetValue("MUIVerb", ShellVerbTitle, RegistryValueKind.String);
        key.SetValue("Icon", icon, RegistryValueKind.String);
        key.SetValue("Position", "Top", RegistryValueKind.String);

        if (supportsMultiSelect)
        {
            key.SetValue("MultiSelectModel", "Player", RegistryValueKind.String);
        }

        using var commandKey = key.CreateSubKey("command", writable: true)
            ?? throw new InvalidOperationException($"无法创建注册表项: HKCU\\Software\\Classes\\{relativeKeyPath}\\command");

        commandKey.SetValue(null, command, RegistryValueKind.String);
    }

    private static void VerifyClassicShellExtension(string expectedExePath)
    {
        var keys = new[]
        {
            @"*\shell\" + ShellVerbName + @"\command",
            @"Directory\shell\" + ShellVerbName + @"\command",
            @"Directory\Background\shell\" + ShellVerbName + @"\command"
        };

        foreach (var keyPath in keys)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + keyPath, writable: false);
            var command = key?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new InvalidOperationException($"右键菜单注册验证失败，缺少命令项: HKCU\\Software\\Classes\\{keyPath}");
            }

            if (!command.Contains("shell-share", StringComparison.OrdinalIgnoreCase) ||
                !command.Contains(expectedExePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"右键菜单注册验证失败，命令不匹配: {command}");
            }
        }

        Console.WriteLine("  右键菜单注册校验通过");
    }

    private static string ResolvePreferredIcon(string fallbackExePath)
    {
        var installDir = TryFindPcManagerInstallDir();
        if (!string.IsNullOrWhiteSpace(installDir))
        {
            var midropIcon = Path.Combine(installDir, "Assets", "midrop_logo.ico");
            if (File.Exists(midropIcon))
            {
                return midropIcon;
            }

            var xiaomiExe = Path.Combine(installDir, "XiaomiPcManager.exe");
            if (File.Exists(xiaomiExe))
            {
                return xiaomiExe + ",0";
            }

            var continuityExe = Path.Combine(installDir, "MiPcContinuity.exe");
            if (File.Exists(continuityExe))
            {
                return continuityExe + ",0";
            }
        }

        return fallbackExePath + ",0";
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

            var dir = Path.GetDirectoryName(value);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                return dir;
            }
        }

        return null;
    }

    private static void TryRestartExplorer()
    {
        var script = string.Join(Environment.NewLine, new[]
        {
            "$p = Get-Process explorer -ErrorAction SilentlyContinue",
            "if ($null -ne $p) { Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue }",
            "Start-Process explorer.exe"
        });

        var result = SystemUtil.RunPowerShell(script);
        if (!result.IsSuccess)
        {
            Console.WriteLine("  Explorer 刷新失败（可手动重启 explorer.exe 后再看右键菜单）");
            return;
        }

        Console.WriteLine("  已刷新 Explorer 进程");
    }
}