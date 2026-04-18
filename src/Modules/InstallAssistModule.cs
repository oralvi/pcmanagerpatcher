using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PCManagerCompatCli.NativePayload;
using PCManagerCompatCli.Infrastructure;

namespace PCManagerCompatCli.Modules;

internal sealed record InstallAssistOptions(
    string WorkspaceRoot,
    string InstallerSearchDir,
    string? InstallerPath,
    string InstallerNameContains,
    bool VerifySignature,
    bool RequireSignatureValid,
    string SignerContains,
    string InstallerArgs);

internal sealed record SignatureInfo(string Status, string Subject);

[SupportedOSPlatform("windows")]
internal sealed class InstallAssistModule
{
    private static readonly int[] AcceptableInstallerExitCodes = { 0, 3010, 1641 };
    private const uint Infinite = 0xFFFFFFFF;
    private const uint CreateSuspended = 0x00000004;
    private const uint MemCommit = 0x00001000;
    private const uint MemRelease = 0x00008000;
    private const uint PageReadWrite = 0x04;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        UIntPtr dwSize,
        uint flAllocationType,
        uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        UIntPtr nSize,
        out UIntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        UIntPtr dwSize,
        uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(
        IntPtr hProcess,
        IntPtr lpThreadAttributes,
        UIntPtr dwStackSize,
        IntPtr lpStartAddress,
        IntPtr lpParameter,
        uint dwCreationFlags,
        out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOW
    {
        public uint cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    public void Execute(InstallAssistOptions options)
    {
        RunInstallAssist(options, isInteractive: false);
    }

    public void ExecuteInteractive(InstallAssistOptions options)
    {
        RunInstallAssist(options, isInteractive: true);
    }

    public string? TryFindInstaller(string searchDir, string installerNameContains)
    {
        if (string.IsNullOrWhiteSpace(searchDir) || !Directory.Exists(searchDir))
        {
            return null;
        }

        var files = Directory.GetFiles(searchDir, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(path =>
                string.IsNullOrWhiteSpace(installerNameContains) ||
                Path.GetFileName(path).Contains(installerNameContains, StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        return files.FirstOrDefault()?.FullName;
    }

    public SignatureInfo GetAuthenticodeSignature(string filePath)
    {
        var script = string.Join(Environment.NewLine, new[]
        {
            "$p = " + SystemUtil.PSQuote(filePath),
            "$sig = Get-AuthenticodeSignature -FilePath $p",
            "$status = [string]$sig.Status",
            "$subject = ''",
            "if ($null -ne $sig.SignerCertificate) { $subject = [string]$sig.SignerCertificate.Subject }",
            "Write-Output $status",
            "Write-Output $subject"
        });

        var result = SystemUtil.RunPowerShell(script);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"无法读取安装包签名: {result.StdErr}");
        }

        var lines = result.StdOut
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .ToList();

        if (lines.Count == 0)
        {
            throw new InvalidOperationException("签名信息为空");
        }

        var status = (lines.ElementAtOrDefault(0) ?? string.Empty).Trim();
        var subject = string.Join(" ", lines.Skip(1)).Trim();
        return new SignatureInfo(status, subject);
    }

    private void RunInstallAssist(InstallAssistOptions options, bool isInteractive)
    {
        var installerPath = ResolveInstallerPath(options, isInteractive);
        Console.WriteLine($"[install-assist] 安装包: {installerPath}");

        if (options.VerifySignature)
        {
            var sig = GetAuthenticodeSignature(installerPath);
            Console.WriteLine($"[install-assist] 签名状态: {sig.Status}");
            Console.WriteLine($"[install-assist] 签名主体: {sig.Subject}");

            if (options.RequireSignatureValid && !string.Equals(sig.Status, "Valid", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"签名状态不是 Valid: {sig.Status}");
            }

            if (!string.IsNullOrWhiteSpace(options.SignerContains) &&
                !sig.Subject.Contains(options.SignerContains, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"签名主体不匹配，期望包含: {options.SignerContains}");
            }
        }

        Console.WriteLine("[install-assist] 正在启动安装器（使用 DLL 注入）...");
        var stopwatch = Stopwatch.StartNew();

        var workingDir = Path.GetDirectoryName(installerPath) ?? ".";
        var hookDllPath = EnsureEmbeddedHookDllExtracted();

        int exitCode;

        try
        {
            exitCode = InjectAndRunInstaller(
                installerPath,
                hookDllPath,
                workingDir,
                options.InstallerArgs);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"DLL 注入失败: {ex.Message}", ex);
        }
        
        stopwatch.Stop();

        if (!AcceptableInstallerExitCodes.Contains(exitCode))
        {
            throw new InvalidOperationException($"安装器退出码异常: {exitCode}");
        }

        Console.WriteLine($"[install-assist] 完成，退出码: {exitCode}");
        Console.WriteLine($"[install-assist] 安装器运行时长: {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    private static string EnsureEmbeddedHookDllExtracted()
    {
        var version = typeof(InstallAssistModule).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var tempDir = Path.Combine(Path.GetTempPath(), "pcmanager-compat", "payloads", version);
        var targetPath = Path.Combine(tempDir, "hook.dll");
        var bytes = Convert.FromBase64String(HookDllPayload.Base64);

        Directory.CreateDirectory(tempDir);

        var tempPath = Path.Combine(tempDir, $"hook_{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(tempPath, bytes);

        File.Copy(tempPath, targetPath, overwrite: true);
        File.Delete(tempPath);

        return targetPath;
    }

    private static int InjectAndRunInstaller(string exePath, string dllPath, string workDir, string installerArgs)
    {
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"目标程序不存在: {exePath}");
        }

        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException($"注入 DLL 不存在: {dllPath}");
        }

        Console.WriteLine($"[install-assist] 目标进程: {exePath}");
        Console.WriteLine($"[install-assist] 注入 DLL: {dllPath}");
        Console.WriteLine($"[install-assist] 工作目录: {workDir}");

        var startupInfo = new STARTUPINFOW { cb = (uint)Marshal.SizeOf<STARTUPINFOW>() };
        var commandLine = BuildCommandLine(exePath, installerArgs);

        if (!CreateProcessW(
                exePath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CreateSuspended,
                IntPtr.Zero,
                workDir,
                ref startupInfo,
                out var processInfo))
        {
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CreateProcessW 失败，错误码: {err} (0x{err:X8})");
        }

        Console.WriteLine($"[install-assist] 进程已创建（暂停状态）: PID={processInfo.dwProcessId}");

        IntPtr remoteDllBuffer = IntPtr.Zero;
        IntPtr remoteThreadHandle = IntPtr.Zero;

        try
        {
            var dllPathBytes = System.Text.Encoding.Unicode.GetBytes(dllPath + "\0");
            remoteDllBuffer = VirtualAllocEx(
                processInfo.hProcess,
                IntPtr.Zero,
                (UIntPtr)dllPathBytes.Length,
                MemCommit,
                PageReadWrite);

            if (remoteDllBuffer == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"VirtualAllocEx 失败，错误码: {err} (0x{err:X8})");
            }

            if (!WriteProcessMemory(
                    processInfo.hProcess,
                    remoteDllBuffer,
                    dllPathBytes,
                    (UIntPtr)dllPathBytes.Length,
                    out _))
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"WriteProcessMemory 失败，错误码: {err} (0x{err:X8})");
            }

            var kernel32Handle = GetModuleHandleW("kernel32.dll");
            if (kernel32Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法获取 kernel32.dll 的模块句柄");
            }

            var loadLibraryAddr = GetProcAddress(kernel32Handle, "LoadLibraryW");
            if (loadLibraryAddr == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"无法获取 LoadLibraryW 地址，错误码: {err} (0x{err:X8})");
            }

            remoteThreadHandle = CreateRemoteThread(
                processInfo.hProcess,
                IntPtr.Zero,
                UIntPtr.Zero,
                loadLibraryAddr,
                remoteDllBuffer,
                0,
                out _);

            if (remoteThreadHandle == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"CreateRemoteThread 失败，错误码: {err} (0x{err:X8})");
            }

            var waitResult = WaitForSingleObject(remoteThreadHandle, Infinite);
            if (waitResult != 0)
            {
                throw new InvalidOperationException($"等待远程线程失败，结果: {waitResult}");
            }

            var previousSuspendCount = ResumeThread(processInfo.hThread);
            if (previousSuspendCount == uint.MaxValue)
            {
                var err = Marshal.GetLastWin32Error();
                Console.WriteLine($"[install-assist] 警告: ResumeThread 失败，错误码: {err} (0x{err:X8})");
            }

            WaitForSingleObject(processInfo.hProcess, Infinite);

            if (!GetExitCodeProcess(processInfo.hProcess, out var exitCode))
            {
                exitCode = 1;
            }

            return (int)exitCode;
        }
        finally
        {
            if (remoteThreadHandle != IntPtr.Zero)
            {
                _ = CloseHandle(remoteThreadHandle);
            }

            if (remoteDllBuffer != IntPtr.Zero)
            {
                _ = VirtualFreeEx(processInfo.hProcess, remoteDllBuffer, UIntPtr.Zero, MemRelease);
            }

            if (processInfo.hProcess != IntPtr.Zero)
            {
                _ = CloseHandle(processInfo.hProcess);
            }

            if (processInfo.hThread != IntPtr.Zero)
            {
                _ = CloseHandle(processInfo.hThread);
            }
        }
    }

    private static string BuildCommandLine(string exePath, string installerArgs)
    {
        if (string.IsNullOrWhiteSpace(installerArgs))
        {
            return $"\"{exePath}\"";
        }

        return $"\"{exePath}\" {installerArgs}".Trim();
    }

    private string ResolveInstallerPath(InstallAssistOptions options, bool isInteractive = false)
    {
        if (!string.IsNullOrWhiteSpace(options.InstallerPath))
        {
            var specified = NormalizePath(options.WorkspaceRoot, options.InstallerPath);
            if (!File.Exists(specified))
            {
                throw new FileNotFoundException($"找不到安装包: {specified}");
            }

            return specified;
        }

        var searchDir = NormalizePath(options.WorkspaceRoot, options.InstallerSearchDir);
        var found = TryFindInstaller(searchDir, options.InstallerNameContains);
        if (!string.IsNullOrWhiteSpace(found))
        {
            return found;
        }

        // 未找到安装器
        if (isInteractive)
        {
            // 菜单模式：提示用户输入
            Console.WriteLine($"[install-assist] 在 {searchDir} 中未找到安装器");
            return PromptForInstallerPath();
        }
        else
        {
            // 命令行模式：直接报错
            throw new FileNotFoundException($"在目录中未找到安装器: {searchDir}");
        }
    }

    private string PromptForInstallerPath()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("请输入安装包路径 (可粘贴路径或拖入文件):");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("[!] 路径不能为空，请重试");
                continue;
            }

            // 处理拖入或粘贴时可能带的引号
            var path = input.Trim().Trim('"', '\'');

            if (!File.Exists(path))
            {
                Console.WriteLine($"[!] 文件不存在: {path}");
                continue;
            }

            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[!] 只能选择 .exe 文件");
                continue;
            }

            Console.WriteLine($"[install-assist] 已确认: {path}");
            return path;
        }
    }

    private static string NormalizePath(string workspaceRoot, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        // 相对路径默认相对于可执行文件所在目录
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }
}
