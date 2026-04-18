using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Win32;

namespace PCManagerCompatCli.Modules;

internal sealed record CameraPopupOptions(
    string WorkspaceRoot,
    string? TargetDllPath,
    string? OutputDllPath,
    string BackupSuffix);

internal sealed record CameraPatchResult(
    string TargetDll,
    string OutputDll,
    string BackupDll,
    int PatchedMethodCount);

internal sealed record CameraStatusResult(
    string TargetDll,
    bool TargetExists,
    string? CompatBackup,
    string? LegacyBackup,
    bool PatchedByCompat);

[SupportedOSPlatform("windows")]
internal sealed class CameraPopupModule
{
    private static readonly string[] CameraMethodNames =
    {
        "ShowCameraToast",
        "CloseCameraToast",
        "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnShowCombinedPrompt",
        "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnCloseCombinedPrompt",
        "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.ExceptionCallback",
        "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.FirstUsedCallback",
        "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnAddLocalCamera",
        "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnRemoveDistributedCamera",
        "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnClearDistributedCamera",
        "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnShowCameraBall",
        "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnUpdateCameraBallState",
        "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnAddDistributedCamera",
        "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnReSetSelectItem",
        "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnOpenCloseCamera",
        "UpdateCameraStatus",
        "AddCameraDevice"
    };

    public CameraPatchResult Patch(CameraPopupOptions options)
    {
        var targetDll = ResolveTargetDll(options.WorkspaceRoot, options.TargetDllPath, mustExist: true);
        var backupDll = EnsureCompatBackup(targetDll, options.BackupSuffix);

        var outputDll = ResolveOutputPath(targetDll, options.OutputDllPath, options.WorkspaceRoot);

        var module = ModuleDefMD.Load(targetDll);
        var synergyType = FindType(module, "PcControlCenter.Services.UI.MainView.Instances.SynergyUIService");

        var patchedCount = 0;
        foreach (var methodName in CameraMethodNames)
        {
            if (!TryFindMethod(synergyType, methodName, out var method) || method == null)
            {
                continue;
            }

            if (TryPatchMethod(method))
            {
                patchedCount++;
            }
        }

        if (patchedCount == 0)
        {
            throw new InvalidOperationException("未找到可补丁的摄像头方法");
        }

        WriteModule(module, targetDll, outputDll);

        return new CameraPatchResult(
            targetDll,
            outputDll,
            backupDll,
            patchedCount);
    }

    public CameraPatchResult Restore(CameraPopupOptions options)
    {
        var targetDll = ResolveTargetDll(options.WorkspaceRoot, options.TargetDllPath, mustExist: false);
        var backup = FindRestoreBackup(targetDll, options.BackupSuffix)
            ?? throw new FileNotFoundException("未找到可用于恢复的备份文件");

        File.Copy(backup, targetDll, overwrite: true);

        return new CameraPatchResult(
            targetDll,
            targetDll,
            backup,
            0);
    }

    public CameraStatusResult Status(CameraPopupOptions options)
    {
        var targetDll = ResolveTargetDll(options.WorkspaceRoot, options.TargetDllPath, mustExist: false);
        var targetExists = File.Exists(targetDll);

        var compatBackup = targetDll + options.BackupSuffix;
        var compatExists = File.Exists(compatBackup);

        var legacyBackup = Path.Combine(Path.GetDirectoryName(targetDll) ?? string.Empty, "PcControlCenter.dll.bak");
        if (!File.Exists(legacyBackup))
        {
            legacyBackup = FindLatestLegacyBackup(targetDll) ?? string.Empty;
        }

        var patchedByCompat = false;
        if (targetExists && compatExists)
        {
            patchedByCompat = !FilesContentEquals(targetDll, compatBackup);
        }

        return new CameraStatusResult(
            targetDll,
            targetExists,
            compatExists ? compatBackup : null,
            string.IsNullOrWhiteSpace(legacyBackup) ? null : legacyBackup,
            patchedByCompat);
    }

    public void Analyze(CameraPopupOptions options)
    {
        var targetDll = ResolveTargetDll(options.WorkspaceRoot, options.TargetDllPath, mustExist: true);
        var module = ModuleDefMD.Load(targetDll);
        var synergyType = FindType(module, "PcControlCenter.Services.UI.MainView.Instances.SynergyUIService");

        Console.WriteLine("camera-popup analyze:");
        Console.WriteLine($"  target: {targetDll}");
        Console.WriteLine($"  type: {synergyType.FullName}");

        foreach (var name in CameraMethodNames)
        {
            if (TryFindMethod(synergyType, name, out var method) && method != null)
            {
                var isRetOnly = IsRetOnlyMethod(method);
                Console.WriteLine($"  - {name}: found{(isRetOnly ? " (ret-only)" : string.Empty)}");
            }
            else
            {
                Console.WriteLine($"  - {name}: not found");
            }
        }
    }

    private static string ResolveTargetDll(string workspaceRoot, string? rawTargetPath, bool mustExist)
    {
        var absWorkspace = Path.GetFullPath(string.IsNullOrWhiteSpace(workspaceRoot) ? "." : workspaceRoot);

        if (!string.IsNullOrWhiteSpace(rawTargetPath))
        {
            var explicitPath = NormalizePath(absWorkspace, rawTargetPath);
            if (mustExist && !File.Exists(explicitPath))
            {
                throw new FileNotFoundException($"目标 DLL 不存在: {explicitPath}");
            }

            return explicitPath;
        }

        var appDirDll = ResolveProgramDirectoryWorkingDllPath();
        if (mustExist && !File.Exists(appDirDll))
        {
            TryExtractInstalledDllToProgramDirectory(appDirDll);
        }

        var candidates = new List<string>
        {
            appDirDll,
            Path.Combine(absWorkspace, "PcControlCenter.dll"),
            Path.Combine(absWorkspace, "samples", "camera-popup", "PcControlCenter.dll")
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(found))
        {
            return found;
        }

        if (!mustExist)
        {
            return candidates[0];
        }

        var installed = TryFindInstalledPcControlCenterDll();
        if (!string.IsNullOrWhiteSpace(installed))
        {
            throw new FileNotFoundException(
                "已定位到安装目录 DLL，但提取到程序同目录失败，请检查当前目录写权限或手动使用 --target-dll 指定。\n" +
                "  install: " + installed + "\n" +
                "  local:   " + appDirDll);
        }

        throw new FileNotFoundException(
            "未找到 PcControlCenter.dll，可通过 --target-dll 指定。已检查:\n" +
            string.Join(Environment.NewLine, candidates.Select(path => "  - " + path)));
    }

    private static string ResolveProgramDirectoryWorkingDllPath()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "PcControlCenter.dll");
    }

    private static void TryExtractInstalledDllToProgramDirectory(string destinationDll)
    {
        var installedDll = TryFindInstalledPcControlCenterDll();
        if (string.IsNullOrWhiteSpace(installedDll))
        {
            return;
        }

        var destinationDir = Path.GetDirectoryName(destinationDll);
        if (string.IsNullOrWhiteSpace(destinationDir))
        {
            return;
        }

        Directory.CreateDirectory(destinationDir);

        try
        {
            File.Copy(installedDll, destinationDll, overwrite: false);
            Console.WriteLine("[camera-popup] 已自动提取安装目录 DLL 到程序同目录");
            Console.WriteLine($"  source: {installedDll}");
            Console.WriteLine($"  local:  {destinationDll}");
        }
        catch (IOException) when (File.Exists(destinationDll))
        {
            // keep existing local working copy; do not overwrite user files
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"无法写入程序目录工作副本: {destinationDll}; {ex.Message}",
                ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"提取安装目录 DLL 失败: {ex.Message}",
                ex);
        }
    }

    private static string? TryFindInstalledPcControlCenterDll()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MI", "XiaomiPCManager"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MI", "XiaomiPCManager")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        var candidates = new List<(string Path, Version Version, DateTime LastWriteUtc)>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var versionDir in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                var dllPath = Path.Combine(versionDir, "PcControlCenter.dll");
                if (!File.Exists(dllPath))
                {
                    continue;
                }

                var versionName = Path.GetFileName(versionDir);
                var parsedVersion = ParseVersionOrZero(versionName);
                var lastWriteUtc = File.GetLastWriteTimeUtc(dllPath);

                candidates.Add((dllPath, parsedVersion, lastWriteUtc));
            }
        }

        var selected = candidates
            .OrderByDescending(item => item.Version)
            .ThenByDescending(item => item.LastWriteUtc)
            .Select(item => item.Path)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(selected))
        {
            return selected;
        }

        var installDir = TryFindPcManagerInstallDir();
        if (string.IsNullOrWhiteSpace(installDir))
        {
            return null;
        }

        var fallback = Path.Combine(installDir, "PcControlCenter.dll");
        return File.Exists(fallback) ? fallback : null;
    }

    private static Version ParseVersionOrZero(string? raw)
    {
        if (Version.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return new Version(0, 0);
    }

    private static string? TryFindPcManagerInstallDir()
    {
        var clsids = new[]
        {
            "{504d69c0-cb52-48df-b5b5-7161829fabc8}",
            "{1bca9901-05c3-4d01-8ad4-78da2eac9b3f}"
        };

        foreach (var clsid in clsids)
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

    private static string EnsureCompatBackup(string targetDll, string backupSuffix)
    {
        var backup = targetDll + backupSuffix;
        if (!File.Exists(backup))
        {
            File.Copy(targetDll, backup, overwrite: false);
        }

        return backup;
    }

    private static string? FindRestoreBackup(string targetDll, string backupSuffix)
    {
        var compat = targetDll + backupSuffix;
        if (File.Exists(compat))
        {
            return compat;
        }

        var legacy = Path.Combine(Path.GetDirectoryName(targetDll) ?? string.Empty, "PcControlCenter.dll.bak");
        if (File.Exists(legacy))
        {
            return legacy;
        }

        return FindLatestLegacyBackup(targetDll);
    }

    private static string? FindLatestLegacyBackup(string targetDll)
    {
        var dir = Path.GetDirectoryName(targetDll);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return null;
        }

        return Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var name = Path.GetFileName(path).ToLowerInvariant();
                return name.StartsWith("pccontrolcenter") && name.Contains("_bak_");
            })
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static TypeDef FindType(ModuleDef module, string fullName)
    {
        return module.GetTypes().FirstOrDefault(t => t.FullName == fullName)
            ?? throw new InvalidOperationException($"未找到目标类型: {fullName}");
    }

    private static bool TryFindMethod(TypeDef type, string methodName, out MethodDef? method)
    {
        method = type.Methods.FirstOrDefault(m => m.Name == methodName);

        if (method == null && methodName.Contains('.'))
        {
            method = type.Methods.FirstOrDefault(m => m.Name.EndsWith("." + methodName, StringComparison.Ordinal));
        }

        if (method == null)
        {
            var shortName = methodName.Split('.').Last();
            method = type.Methods.FirstOrDefault(m => m.Name == shortName || m.Name.EndsWith("." + shortName, StringComparison.Ordinal));
        }

        return method != null;
    }

    private static bool TryPatchMethod(MethodDef method)
    {
        if (method.IsAbstract || method.Body == null)
        {
            return false;
        }

        try
        {
            var body = method.Body;
            ReplaceMethodBodyWithRetOnly(body, method);
            return true;
        }
        catch
        {
            try
            {
                ReplaceMethodBodyWithRetOnly(method.Body!, method);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static void ReplaceMethodBodyWithRetOnly(CilBody body, MethodDef method)
    {
        body.ExceptionHandlers.Clear();
        body.Instructions.Clear();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.SimplifyMacros(method.Parameters);
        body.KeepOldMaxStack = true;
    }

    private static bool IsRetOnlyMethod(MethodDef method)
    {
        var body = method.Body;
        if (body == null)
        {
            return false;
        }

        return body.Instructions.Count == 1 && body.Instructions[0].OpCode == OpCodes.Ret;
    }

    private static void WriteModule(ModuleDefMD module, string targetDll, string outputDll)
    {
        var outputDir = Path.GetDirectoryName(outputDll);
        if (!string.IsNullOrWhiteSpace(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        if (Path.GetFullPath(outputDll).Equals(Path.GetFullPath(targetDll), StringComparison.OrdinalIgnoreCase))
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "pcmanager-compat", "camera-popup");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, $"PcControlCenter_PATCHED_{Guid.NewGuid():N}.dll");

            module.Write(tempPath);
            File.Copy(tempPath, targetDll, overwrite: true);
            File.Delete(tempPath);
            return;
        }

        var finalOutput = EnsureNonConflictingOutput(outputDll);
        module.Write(finalOutput);
    }

    private static string EnsureNonConflictingOutput(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        var index = 1;
        while (true)
        {
            var candidate = Path.Combine(dir, $"{name}_{index}{ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private static bool FilesContentEquals(string leftPath, string rightPath)
    {
        return ComputeSha256(leftPath).Equals(ComputeSha256(rightPath), StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private static string ResolveOutputPath(string targetDll, string? outputDllPath, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(outputDllPath))
        {
            return targetDll;
        }

        return NormalizePath(workspaceRoot, outputDllPath);
    }

    private static string NormalizePath(string workspaceRoot, string rawPath)
    {
        if (Path.IsPathRooted(rawPath))
        {
            return rawPath;
        }

        return Path.GetFullPath(Path.Combine(workspaceRoot, rawPath));
    }
}
