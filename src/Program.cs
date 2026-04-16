using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace CecilDll;

internal partial class Program
{
    private static void Main()
    {
        try
        {
            // 获取应用路径
            string dllPathPattern = @"C:\Program Files\MI\XiaomiPCManager\*\PcControlCenter.dll";

            // 关闭小米电脑管家进程
            Console.WriteLine("正在关闭小米电脑管家进程...");
            KillProcessByName("XiaomiPCManager");

            var rawPath = dllPathPattern.Trim().Trim('\"');
            var dllPath = ResolveLatestVersionPath(rawPath);
            Console.WriteLine($"找到最新版本路径：{dllPath},请确认该路径是否正确(Y/N)");
            var flag = Console.ReadLine();
            if (flag != "y" && flag != "Y")
            {
                Console.WriteLine("请输入dll路径(例如:C:\\Program Files\\MI\\XiaomiPCManager\\*\\PcControlCenter.dll)");
                dllPath = Console.ReadLine();
            }

            // 在修改前备份原始 DLL，避免直接覆盖导致无法回退
            var backupPath = CreateBackup(dllPath);
            Console.WriteLine($"已完成备份：{backupPath}");

            var module = ModuleDefMD.Load(dllPath);
            
            // 精准补丁：针对摄像头协同服务（Synergy）的方法做拦截，避免影响蓝牙耳机等其他弹窗
            Console.WriteLine("\n=== 应用精准摄像头补丁 ===");
            var synergyType = FindType(module, "PcControlCenter.Services.UI.MainView.Instances.SynergyUIService");
            var showCameraToastMethod = FindMethod(synergyType, "ShowCameraToast");
            Console.WriteLine($"找到目标方法：{showCameraToastMethod.FullName}");
            ModifyCameraToastMethod(showCameraToastMethod);
            Console.WriteLine("摄像头弹窗补丁已应用");

            Console.WriteLine("\n请选择是直接替换还是生成到该目录");
            Console.WriteLine("1.直接替换");
            Console.WriteLine("2.生成到该目录");
            var input = Console.ReadLine();
            string outputDllPath = $"{Path.GetFileNameWithoutExtension(dllPath)}.dll";
            switch (input)
            {
                case "1":
                    outputDllPath = dllPath;
                    break;
                case "2":
                    outputDllPath = $"{Path.GetFileNameWithoutExtension(dllPath)}.dll";
                    break;
                default:
                    Console.WriteLine("输入错误,现在已经默认保存到该目录");
                    break;
            }

            // 保存修改后的DLL
            module.Write(outputDllPath);

            Console.WriteLine($"修改成功！新DLL已保存至：{outputDllPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"操作失败：{ex.Message}");
        }

        // 按任意键退出
        Console.WriteLine("按任意键退出...");
        Console.ReadLine();
    }

    // 查找目标类型
    private static TypeDef FindType(ModuleDef module, string className)
    {
        var targetType = module.GetTypes().FirstOrDefault(t => t.FullName == className);
        if (targetType == null)
            throw new ArgumentException($"未找到类：{className}");
        return targetType;
    }

    // 查找目标方法
    private static MethodDef FindMethod(TypeDef type, string methodName)
    {
        var targetMethod = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (targetMethod == null)
            throw new ArgumentException($"未找到方法：{methodName}");

        if (targetMethod.IsAbstract)
            throw new NotSupportedException("无法修改抽象方法");
        return targetMethod;
    }

    // 修改摄像头弹窗方法：直接提前返回，不执行任何通知逻辑
    // 这样可以完全隔离摄像头路径，不影响蓝牙耳机等其他服务的弹窗
    private static void ModifyCameraToastMethod(MethodDef method)
    {
        var methodBody = method.Body;
        
        // 清空原始指令，仅保留 Ret，相当于方法直接返回
        methodBody.Instructions.Clear();
        methodBody.Instructions.Add(Instruction.Create(OpCodes.Ret));
        
        // 简化宏指令并清除堆栈计算缓存
        methodBody.SimplifyMacros(method.Parameters);
        methodBody.KeepOldMaxStack = true;
        
        Console.WriteLine($"  -> 方法已设置为直接返回，摄像头弹窗将被完全屏蔽");
    }
    // 新增路径解析方法
    private static string ResolveLatestVersionPath(string rawPath)
    {
        var adjustedPath = rawPath.Replace("\\*", "\\");
        var baseDir = Path.GetDirectoryName(adjustedPath);
        var fileName = Path.GetFileName(adjustedPath);

        var versionDirs = Directory.EnumerateDirectories(baseDir)
            .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d+\.\d+\.\d+\.\d+$"))
            .OrderByDescending(d => Version.Parse(Path.GetFileName(d)))
            .ToList();

        if (versionDirs.Count == 0)
            throw new DirectoryNotFoundException("未找到任何版本目录");

        var latestVersionDir = versionDirs.First();
        return Path.Combine(latestVersionDir, fileName);
    }

    // 为目标 DLL 生成唯一备份文件，避免覆盖已有备份
    private static string CreateBackup(string dllPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath))
            throw new ArgumentException("DLL 路径不能为空");
        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"找不到 DLL：{dllPath}");

        var directory = Path.GetDirectoryName(dllPath) ?? throw new InvalidOperationException("无法解析 DLL 目录");
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(dllPath);
        var extension = Path.GetExtension(dllPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        var backupPath = Path.Combine(directory, $"{fileNameWithoutExt}.backup_{timestamp}{extension}");
        var index = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(directory, $"{fileNameWithoutExt}.backup_{timestamp}_{index}{extension}");
            index++;
        }

        File.Copy(dllPath, backupPath, overwrite: false);
        return backupPath;
    }

    // 关闭应用进程

    public static void KillProcessByName(string processName)
    {
        // 获取所有匹配进程
        var processes = Process.GetProcessesByName(processName);

        foreach (var process in processes)
        {
            try
            {
                process.CloseMainWindow();

                // 若未退出则使用强制终止
                if (!process.WaitForExit(2000))
                {
                    process.Kill();
                    process.WaitForExit();
                }

                Console.WriteLine($"成功终止进程：{process.ProcessName} (PID: {process.Id})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"终止进程失败：{ex.Message},请手动关闭");
            }
        }
    }
}