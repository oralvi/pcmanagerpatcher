using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using PCManagerCompatCli.Infrastructure;
using PCManagerCompatCli.Modules;

namespace PCManagerCompatCli;

[SupportedOSPlatform("windows")]
internal sealed class CliApp
{
    private readonly InstallAssistModule installAssist = new();
    private readonly ExtensionSetupModule extensionSetup = new();
    private readonly SystemServiceRepairModule systemServiceRepair = new();
    private readonly CameraPopupModule cameraPopup = new();
    private readonly ShellShareModule shellShare = new();

    public int Run(string[] args)
    {
        if (!EnsureAdminAtStartup(args))
        {
            return 0;
        }

        if (args.Any(arg => string.Equals(arg, "--non-interactive", StringComparison.OrdinalIgnoreCase)))
        {
            return RunLegacyNonInteractive(args);
        }

        if (args.Any(arg => string.Equals(arg, "--analyze", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "-a", StringComparison.OrdinalIgnoreCase)))
        {
            return RunLegacyAnalyze(args);
        }

        if (args.Length == 0)
        {
            return RunMenu();
        }

        var command = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        return command switch
        {
            "install-assist" => RunInstallAssist(rest),
            "extension-setup" => RunExtensionSetup(rest),
            "repair-system" => RunRepairSystem(rest),
            "camera-popup" => RunCameraPopup(rest),
            "shell-share" => RunShellShare(rest),
            "menu" => RunMenu(),
            "help" or "-h" or "--help" => PrintHelpAndReturn(),
            _ => throw new ArgumentException($"未知命令: {command}")
        };
    }

    private int RunLegacyNonInteractive(IReadOnlyList<string> args)
    {
        var parsed = ParseArgs(args);
        var input = GetNullable(parsed.Options, "input")
            ?? throw new ArgumentException("--non-interactive 需要 --input <dll-path>");
        var output = GetNullable(parsed.Options, "output")
            ?? throw new ArgumentException("--non-interactive 需要 --output <dll-path>");

        var options = new CameraPopupOptions(
            WorkspaceRoot: Path.GetFullPath("."),
            TargetDllPath: input,
            OutputDllPath: output,
            BackupSuffix: ".compatbak");

        var result = cameraPopup.Patch(options);
        Console.WriteLine($"Patched methods: {result.PatchedMethodCount}");
        Console.WriteLine($"Output: {result.OutputDll}");
        return 0;
    }

    private int RunLegacyAnalyze(IReadOnlyList<string> args)
    {
        var targetPath = args
            .FirstOrDefault(arg =>
                !arg.StartsWith("-", StringComparison.Ordinal) &&
                File.Exists(arg));

        var options = new CameraPopupOptions(
            WorkspaceRoot: Path.GetFullPath("."),
            TargetDllPath: targetPath,
            OutputDllPath: null,
            BackupSuffix: ".compatbak");

        cameraPopup.Analyze(options);
        return 0;
    }

    private int RunInstallAssist(string[] args)
    {
        var parsed = ParseArgs(args);
        var isInteractive = GetBool(parsed.Options, "interactive", false);

        var workspaceRoot = Path.GetFullPath(GetString(parsed.Options, "workspace", ResolveDefaultWorkspaceRoot()));
        var options = new InstallAssistOptions(
            WorkspaceRoot: workspaceRoot,
            InstallerSearchDir: GetString(parsed.Options, "installer-dir", "."),
            InstallerPath: GetNullable(parsed.Options, "installer-path"),
            InstallerNameContains: GetString(parsed.Options, "installer-name-contains", "XiaomiPCManager"),
            VerifySignature: GetBool(parsed.Options, "verify-signature", true),
            RequireSignatureValid: GetBool(parsed.Options, "require-signature-valid", true),
            SignerContains: GetString(parsed.Options, "signer-contains", "Xiaomi Communications Co., Ltd."),
            InstallerArgs: GetString(parsed.Options, "installer-args", string.Empty));

        if (isInteractive)
        {
            installAssist.ExecuteInteractive(options);
        }
        else
        {
            installAssist.Execute(options);
        }

        return 0;
    }

    private int RunExtensionSetup(string[] args)
    {
        var parsed = ParseArgs(args);
        var applyRegistryProfile = GetBool(parsed.Options, "apply-registry-profile", true);

        var workspaceRoot = Path.GetFullPath(GetString(parsed.Options, "workspace", ResolveDefaultWorkspaceRoot()));
        var options = new ExtensionSetupOptions(
            WorkspaceRoot: workspaceRoot,
            ApplyRegistryProfile: applyRegistryProfile,
            ProductModel: GetString(parsed.Options, "product-model", "TM2424"),
            InstallShellExtension: GetBool(parsed.Options, "install-shell-extension", true),
            VerifyShellExtension: GetBool(parsed.Options, "verify-shell-extension", true),
            RestartExplorerAfterInstall: GetBool(parsed.Options, "restart-explorer", true));

        extensionSetup.Execute(options);
        return 0;
    }

    private int RunShellShare(string[] args)
    {
        shellShare.Execute(args);
        return 0;
    }

    private int RunRepairSystem(string[] args)
    {
        var parsed = ParseArgs(args);
        var options = new SystemServiceRepairOptions(
            RepairWindowsServiceStack: GetBool(parsed.Options, "repair-windows", true),
            RepairContinuityServices: GetBool(parsed.Options, "repair-continuity", true),
            InstallWebView2Online: GetBool(parsed.Options, "install-webview2", true),
            ForceInstallWebView2: GetBool(parsed.Options, "force-webview2", false),
            WebView2BootstrapperUrl: GetString(parsed.Options, "webview2-url", "https://go.microsoft.com/fwlink/p/?LinkId=2124703"),
            ServiceStartTimeoutSeconds: GetInt(parsed.Options, "service-timeout-seconds", 20));

        systemServiceRepair.Execute(options);
        return 0;
    }

    private int RunCameraPopup(string[] args)
    {
        var parsed = ParseArgs(args);
        var action = parsed.Positionals.FirstOrDefault()?.ToLowerInvariant() ?? "status";

        var workspaceRoot = Path.GetFullPath(GetString(parsed.Options, "workspace", ResolveDefaultWorkspaceRoot()));
        var options = new CameraPopupOptions(
            WorkspaceRoot: workspaceRoot,
            TargetDllPath: GetNullable(parsed.Options, "target-dll"),
            OutputDllPath: GetNullable(parsed.Options, "output"),
            BackupSuffix: GetString(parsed.Options, "backup-suffix", ".compatbak"));

        switch (action)
        {
            case "patch":
            {
                var result = cameraPopup.Patch(options);
                Console.WriteLine("camera-popup patch completed");
                Console.WriteLine($"  target: {result.TargetDll}");
                Console.WriteLine($"  output: {result.OutputDll}");
                Console.WriteLine($"  backup: {result.BackupDll}");
                Console.WriteLine($"  patched methods: {result.PatchedMethodCount}");
                break;
            }
            case "restore":
            {
                var result = cameraPopup.Restore(options);
                Console.WriteLine("camera-popup restore completed");
                Console.WriteLine($"  target: {result.TargetDll}");
                Console.WriteLine($"  restored from: {result.BackupDll}");
                break;
            }
            case "status":
            {
                var status = cameraPopup.Status(options);
                Console.WriteLine("camera-popup status");
                Console.WriteLine($"  target: {status.TargetDll}");
                Console.WriteLine($"  target exists: {status.TargetExists}");
                Console.WriteLine($"  compat backup: {status.CompatBackup ?? "<none>"}");
                Console.WriteLine($"  legacy backup: {status.LegacyBackup ?? "<none>"}");
                Console.WriteLine($"  patched by compat: {status.PatchedByCompat}");
                break;
            }
            case "analyze":
            {
                cameraPopup.Analyze(options);
                break;
            }
            default:
                throw new ArgumentException($"camera-popup 不支持的动作: {action}");
        }

        return 0;
    }

    private int RunMenu()
    {
        while (true)
        {
            TryClearConsole();
            Console.WriteLine();
            Console.WriteLine("================ PCManager Compat CLI ================");
            Console.WriteLine("1) install-assist      协助非小米笔记本安装官方小米电脑管家");
            Console.WriteLine("2) extension-setup     右键菜单扩展安装 + 机型注册表写入");
            Console.WriteLine("3) repair-system       修复系统/服务 + 在线安装 WebView2");
            Console.WriteLine("4) camera-popup patch  摄像头弹窗屏蔽");
            Console.WriteLine("5) camera-popup restore");
            Console.WriteLine("6) camera-popup status");
            Console.WriteLine("7) help");
            Console.WriteLine("0) exit");
            Console.Write("select> ");

            var input = (Console.ReadLine() ?? string.Empty).Trim();

            switch (input)
            {
                case "1":
                {
                    ExecuteMenuCommand(new[]
                    {
                        "install-assist",
                        "--interactive"
                    });
                    break;
                }
                case "2":
                {
                    var productModel = Prompt("product-model", "TM2424");
                    ExecuteMenuCommand(new[]
                    {
                        "extension-setup",
                        "--product-model", productModel
                    });
                    break;
                }
                case "3":
                {
                    RunRepairSystemSubMenu();
                    break;
                }
                case "4":
                {
                    var target = Prompt("target-dll (空=自动提取到程序目录并使用本地副本)", string.Empty);
                    var list = new List<string> { "camera-popup", "patch" };
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        list.Add("--target-dll");
                        list.Add(target);
                    }

                    ExecuteMenuCommand(list.ToArray());
                    break;
                }
                case "5":
                {
                    ExecuteMenuCommand(new[] { "camera-popup", "restore" });
                    break;
                }
                case "6":
                {
                    ExecuteMenuCommand(new[] { "camera-popup", "status" });
                    break;
                }
                case "7":
                    PrintHelpAndReturn();
                    PauseForMenuReturn();
                    break;
                case "0":
                    return 0;
                default:
                    Console.WriteLine("unknown selection");
                    PauseForMenuReturn();
                    break;
            }
        }
    }

    private void RunRepairSystemSubMenu()
    {
        while (true)
        {
            TryClearConsole();
            Console.WriteLine();
            Console.WriteLine("---------------- repair-system ----------------");
            Console.WriteLine("1) 修复 Windows（DISM）");
            Console.WriteLine("2) 修复互联互通服务");
            Console.WriteLine("3) 在线安装 WebView2 Runtime");
            Console.WriteLine("4) 全部修复（Windows-DISM + 互联服务 + WebView2）");
            Console.WriteLine("0) 返回主菜单");
            Console.Write("repair> ");

            var input = (Console.ReadLine() ?? string.Empty).Trim();
            switch (input)
            {
                case "1":
                    ExecuteMenuCommand(new[]
                    {
                        "repair-system",
                        "--repair-windows", "true",
                        "--repair-continuity", "false",
                        "--install-webview2", "false"
                    });
                    break;
                case "2":
                    ExecuteMenuCommand(new[]
                    {
                        "repair-system",
                        "--repair-windows", "false",
                        "--repair-continuity", "true",
                        "--install-webview2", "false"
                    });
                    break;
                case "3":
                {
                    var forceWebView2 = Prompt("force-webview2 (true/false)", "false");
                    ExecuteMenuCommand(new[]
                    {
                        "repair-system",
                        "--repair-windows", "false",
                        "--repair-continuity", "false",
                        "--install-webview2", "true",
                        "--force-webview2", forceWebView2
                    });
                    break;
                }
                case "4":
                {
                    var forceWebView2 = Prompt("force-webview2 (true/false)", "false");
                    ExecuteMenuCommand(new[]
                    {
                        "repair-system",
                        "--repair-windows", "true",
                        "--repair-continuity", "true",
                        "--install-webview2", "true",
                        "--force-webview2", forceWebView2
                    });
                    break;
                }
                case "0":
                    return;
                default:
                    Console.WriteLine("unknown selection");
                    PauseForMenuReturn();
                    break;
            }
        }
    }

    private void ExecuteMenuCommand(string[] commandArgs)
    {
        TryClearConsole();
        Console.WriteLine($"> {string.Join(" ", commandArgs)}");
        Console.WriteLine(new string('-', 64));

        try
        {
            _ = Run(commandArgs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"error: {ex.Message}");
        }

        PauseForMenuReturn();
    }

    private static void PauseForMenuReturn()
    {
        Console.WriteLine();
        Console.Write("按 Enter 返回菜单...");
        Console.ReadLine();
    }

    private static void TryClearConsole()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        try
        {
            Console.Clear();
        }
        catch
        {
            // ignore clear failures in non-interactive hosts
        }
    }

    private static string Prompt(string label, string defaultValue)
    {
        Console.Write($"{label} [{defaultValue}]: ");
        var line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            return defaultValue;
        }

        return line.Trim();
    }

    private static int PrintHelpAndReturn()
    {
        Console.WriteLine("PCManagerCompat");
        Console.WriteLine();
        Console.WriteLine("主要实现：");
        Console.WriteLine("  1) install-assist   安装协助");
        Console.WriteLine("  2) extension-setup  右键菜单扩展安装 + 注册表配置");
        Console.WriteLine("  3) repair-system    系统/服务修复 + WebView2 安装");
        Console.WriteLine("  4) camera-popup     摄像头弹窗子模块（patch/restore/status/analyze）");
        Console.WriteLine("  5) shell-share      Shell Share 相关处理");
        Console.WriteLine();
        Console.WriteLine("示例：");
        Console.WriteLine("  PCManagerPatcher.exe install-assist --installer-dir .");
        Console.WriteLine("  PCManagerPatcher.exe install-assist --installer-dir . --installer-args \"/S\"");
        Console.WriteLine("  PCManagerPatcher.exe extension-setup --workspace . --product-model TM2424");
        Console.WriteLine("  PCManagerPatcher.exe extension-setup --apply-registry-profile false");
        Console.WriteLine("  PCManagerPatcher.exe repair-system --install-webview2 true");
        Console.WriteLine("  PCManagerPatcher.exe repair-system --repair-windows true");
        Console.WriteLine("  PCManagerPatcher.exe camera-popup patch --target-dll samples/camera-popup/PcControlCenter.dll");
        Console.WriteLine("  PCManagerPatcher.exe camera-popup patch   (默认自动提取 Program Files\\MI\\XiaomiPCManager\\<version>\\PcControlCenter.dll 到程序目录)");
        Console.WriteLine("  PCManagerPatcher.exe shell-share");
        Console.WriteLine("  PCManagerPatcher.exe menu");
        return 0;
    }

    private static bool EnsureAdminAtStartup(IReadOnlyList<string> rawArgs)
    {
        if (SystemUtil.IsAdministrator())
        {
            return true;
        }

        Console.WriteLine("当前进程未提权，正在请求管理员权限...");
        SystemUtil.RelaunchAsAdmin(rawArgs);
        return false;
    }

    private static string ResolveDefaultWorkspaceRoot()
    {
        var startPoints = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var start in startPoints)
        {
            var found = TryFindWorkspaceRoot(start);
            if (!string.IsNullOrWhiteSpace(found))
            {
                return found;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string? TryFindWorkspaceRoot(string startPath)
    {
        var current = Path.GetFullPath(startPath);

        while (!string.IsNullOrWhiteSpace(current))
        {
            var hasGit = Directory.Exists(Path.Combine(current, ".git"));
            var hasSrc = Directory.Exists(Path.Combine(current, "src"));
            var hasAnal = Directory.Exists(Path.Combine(current, "anal"));

            if (hasGit || (hasSrc && hasAnal))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return null;
    }

    private static ParsedArgs ParseArgs(IReadOnlyList<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(token);
                continue;
            }

            var key = token[2..];
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[key] = args[i + 1];
                i++;
            }
            else
            {
                options[key] = "true";
            }
        }

        return new ParsedArgs(options, positionals);
    }

    private static string GetString(IReadOnlyDictionary<string, string> options, string key, string defaultValue)
    {
        return options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : defaultValue;
    }

    private static string? GetNullable(IReadOnlyDictionary<string, string> options, string key)
    {
        if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static bool GetBool(IReadOnlyDictionary<string, string> options, string key, bool defaultValue)
    {
        if (!options.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "yes" or "y" or "on" => true,
            "0" or "no" or "n" or "off" => false,
            _ => defaultValue
        };
    }

    private static int GetInt(IReadOnlyDictionary<string, string> options, string key, int defaultValue)
    {
        if (!options.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return int.TryParse(raw.Trim(), out var value) && value > 0
            ? value
            : defaultValue;
    }

    private static int GetNonNegativeInt(IReadOnlyDictionary<string, string> options, string key, int defaultValue)
    {
        if (!options.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return int.TryParse(raw.Trim(), out var value) && value >= 0
            ? value
            : defaultValue;
    }

    private sealed record ParsedArgs(
        Dictionary<string, string> Options,
        List<string> Positionals);
}
