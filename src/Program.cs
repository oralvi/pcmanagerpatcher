using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace CecilDll;

internal partial class Program
{
    private static void Main(string[] args)
    {
        // 检查管理员权限，不足则提权
        if (!IsRunningAsAdmin())
        {
            Console.WriteLine("需要管理员权限，正在重新启动...");
            ElevateToAdmin(args);
            return;
        }

        try
        {
            // 获取应用路径
            string dllPathPattern = @"C:\Program Files\MI\XiaomiPCManager\*\PcControlCenter.dll";

            // 关闭小米电脑管家进程
            Console.WriteLine("正在关闭小米电脑管家进程...");
            KillProcessByName("XiaomiPCManager");
            Console.WriteLine("等待进程完全释放文件（3 秒）...");
            System.Threading.Thread.Sleep(3000);  // 等待更长时间确保文件被释放

            var rawPath = dllPathPattern.Trim().Trim('\"');
            var dllPath = ResolveLatestVersionPath(rawPath);
            Console.WriteLine($"\n=== DLL 路径确认 ===");
            Console.WriteLine($"找到的 DLL 路径：");
            Console.WriteLine($"  {dllPath}");
            Console.WriteLine($"\n是否继续？(Y/N，默认 Y)");
            var flag = Console.ReadLine();
            if (flag?.ToLower() == "n")
            {
                Console.WriteLine("请输入自定义 DLL 路径：");
                dllPath = Console.ReadLine() ?? dllPath;
            }

            if (string.IsNullOrWhiteSpace(dllPath))
                throw new ArgumentException("DLL 路径不能为空");

            // 在修改前备份原始 DLL，避免直接覆盖导致无法回退
            var backupPath = CreateBackup(dllPath);
            Console.WriteLine($"✓ 已完成备份：{backupPath}");

            var module = ModuleDefMD.Load(dllPath);
            
            // 精准补丁：针对摄像头协同服务（Synergy）的方法做拦截，避免影响蓝牙耳机等其他弹窗
            Console.WriteLine("\n=== 应用精准摄像头补丁 ===");
            var synergyType = FindType(module, "PcControlCenter.Services.UI.MainView.Instances.SynergyUIService");
            var showCameraToastMethod = FindMethod(synergyType, "ShowCameraToast");
            Console.WriteLine($"找到目标方法：{showCameraToastMethod.FullName}");
            ModifyCameraToastMethod(showCameraToastMethod);
            Console.WriteLine("摄像头弹窗补丁已应用");

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("请选择保存方式");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"1. 直接替换原文件 ({dllPath})");
            Console.WriteLine($"2. 生成新文件到当前目录 (PcControlCenter.dll)");
            Console.WriteLine("请输入选择（1 或 2）：");
            
            var input = Console.ReadLine();
            string outputDllPath = $"{Path.GetFileNameWithoutExtension(dllPath)}.dll";
            
            switch (input)
            {
                case "1":
                    outputDllPath = dllPath;
                    Console.WriteLine($"\n⚠ 警告：您选择了直接替换原文件");
                    Console.WriteLine($"  备份已保存至：{backupPath}");
                    Console.WriteLine($"  如有问题可从备份恢复");
                    Console.WriteLine($"\n确认替换？(Y/N)：");
                    var confirm = Console.ReadLine();
                    if (confirm?.ToLower() != "y")
                    {
                        Console.WriteLine("已取消直接替换，改为生成新文件");
                        outputDllPath = $"{Path.GetFileNameWithoutExtension(dllPath)}.dll";
                    }
                    else
                    {
                        Console.WriteLine($"✓ 已确认：将直接替换原文件");
                    }
                    break;
                case "2":
                    outputDllPath = $"{Path.GetFileNameWithoutExtension(dllPath)}.dll";
                    Console.WriteLine($"✓ 已选择：生成新文件");
                    Console.WriteLine($"  输出路径：{Path.Combine(Directory.GetCurrentDirectory(), outputDllPath)}");
                    break;
                default:
                    Console.WriteLine("输入无效，已默认选择：生成新文件到当前目录");
                    outputDllPath = $"{Path.GetFileNameWithoutExtension(dllPath)}.dll";
                    break;
            }

            // 保存修改后的DLL（使用重试机制应对文件占用）
            Console.WriteLine($"\n开始写入 DLL 到：{outputDllPath}");
            WriteModuleWithRetry(module, outputDllPath, maxRetries: 5);

            // 验证文件是否真的被修改
            if (File.Exists(outputDllPath))
            {
                var fileInfo = new FileInfo(outputDllPath);
                var modifyTime = fileInfo.LastWriteTime;
                Console.WriteLine($"✓ 文件已确认存在，修改时间：{modifyTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"✓ 文件大小：{fileInfo.Length / 1024} KB");
                Console.WriteLine($"\n修改成功！新DLL已保存至：{outputDllPath}");
            }
            else
            {
                throw new FileNotFoundException($"写入后文件不存在：{outputDllPath}");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"❌ 操作失败 - 文件被占用：{ex.Message}");
            Console.WriteLine("可能的解决方案：");
            Console.WriteLine("  1. 确保小米电脑管家已完全关闭（检查任务管理器）");
            Console.WriteLine("  2. 重启计算机，使用\"直接替换\"选项");
            Console.WriteLine("  3. 禁用杀毒软件并重试");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 操作失败：{ex.Message}");
            Console.WriteLine($"异常类型：{ex.GetType().Name}");
        }

        // 按任意键退出
        Console.WriteLine("按任意键退出...");
        Console.ReadLine();
    }

    /// <summary>
    /// 检查当前进程是否以管理员身份运行
    /// </summary>
    /// <returns>如果以管理员身份运行返回 true，否则返回 false</returns>
    private static bool IsRunningAsAdmin()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 以管理员身份重新启动当前应用
    /// </summary>
    /// <param name="args">命令行参数</param>
    private static void ElevateToAdmin(string[] args)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "",
                UseShellExecute = true,
                Verb = "runas",  // 请求提升权限
                Arguments = args.Length > 0 ? string.Join(" ", args) : ""
            };

            using (var process = Process.Start(processInfo))
            {
                process?.WaitForExit();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"提权失败：{ex.Message}");
            Environment.Exit(1);
        }
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
        var baseDir = Path.GetDirectoryName(adjustedPath) ?? throw new DirectoryNotFoundException($"无法解析路径目录：{adjustedPath}");
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

    // 为目标 DLL 生成备份文件（后缀为 .dllbk）
    private static string CreateBackup(string dllPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath))
            throw new ArgumentException("DLL 路径不能为空");
        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"找不到 DLL：{dllPath}");

        var directory = Path.GetDirectoryName(dllPath) ?? throw new InvalidOperationException("无法解析 DLL 目录");
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(dllPath);
        
        // 后缀改为 .dllbk
        var backupPath = Path.Combine(directory, $"{fileNameWithoutExt}.dllbk");
        var index = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(directory, $"{fileNameWithoutExt}_{index}.dllbk");
            index++;
        }

        File.Copy(dllPath, backupPath, overwrite: false);
        return backupPath;
    }

    // 关闭应用进程

    /// <summary>
    /// 以重试机制写入修改后的 DLL，处理文件被占用的情况
    /// </summary>
    /// <param name="module">要写入的模块</param>
    /// <param name="outputPath">输出路径</param>
    /// <param name="maxRetries">最大重试次数</param>
    /// <param name="delayMs">每次重试之间的延迟（毫秒）</param>
    private static void WriteModuleWithRetry(ModuleDef module, string outputPath, int maxRetries = 5, int delayMs = 2000)
    {
        // 记录原始文件的修改时间
        DateTime originalModifyTime = DateTime.MinValue;
        if (File.Exists(outputPath))
        {
            var fileInfo = new FileInfo(outputPath);
            originalModifyTime = fileInfo.LastWriteTime;
            Console.WriteLine($"原文件修改时间：{originalModifyTime:yyyy-MM-dd HH:mm:ss}");
        }

        Exception? lastException = null;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                Console.WriteLine($"尝试写入 DLL（第 {i + 1}/{maxRetries} 次）...");
                module.Write(outputPath);

                // 验证文件是否真的被修改
                if (File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    DateTime newModifyTime = fileInfo.LastWriteTime;
                    
                    // 检查修改时间是否改变
                    if (newModifyTime > originalModifyTime || !File.Exists(outputPath.Replace(".dll", "_bak.dll")))
                    {
                        Console.WriteLine($"✓ DLL 已成功写入，新的修改时间：{newModifyTime:yyyy-MM-dd HH:mm:ss}");
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"⚠ 警告：文件修改时间未变化，写入可能失败");
                        throw new IOException("DLL 文件写入失败：修改时间未更新");
                    }
                }
                else
                {
                    throw new FileNotFoundException($"写入后文件不存在：{outputPath}");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
                if (i < maxRetries - 1)
                {
                    Console.WriteLine($"⚠ 文件被占用（{ex.Message}），等待 {delayMs}ms 后重试（{i + 1}/{maxRetries}）...");
                    System.Threading.Thread.Sleep(delayMs);
                }
                else
                {
                    Console.WriteLine($"✗ 所有尝试均失败，文件始终被占用");
                }
            }
            catch (IOException ex)
            {
                lastException = ex;
                if (i < maxRetries - 1)
                {
                    Console.WriteLine($"⚠ IO 错误（{ex.Message}），等待 {delayMs}ms 后重试（{i + 1}/{maxRetries}）...");
                    System.Threading.Thread.Sleep(delayMs);
                }
                else
                {
                    Console.WriteLine($"✗ 所有尝试均失败，IO 错误：{ex.Message}");
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (i < maxRetries - 1)
                {
                    Console.WriteLine($"⚠ 写入失败（{ex.GetType().Name}: {ex.Message}），等待后重试...");
                    System.Threading.Thread.Sleep(delayMs);
                }
                else
                {
                    Console.WriteLine($"✗ 所有尝试均失败，错误：{ex.Message}");
                }
            }
        }

        throw lastException ?? new Exception("未知错误：无法写入 DLL");
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