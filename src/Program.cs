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
        try
        {
            Console.WriteLine("╔════════════════════════════════════════════════════╗");
            Console.WriteLine("║   小米电脑管家 - 摄像头弹窗屏蔽补丁工具              ║");
            Console.WriteLine("║   PCManager Patcher v1.0.10                        ║");
            Console.WriteLine("╚════════════════════════════════════════════════════╝\n");

            // 步骤 1：查找同目录中的 DLL 文件
            string currentDir = Environment.CurrentDirectory;
            string dllPath = Path.Combine(currentDir, "PcControlCenter.dll");

            if (!File.Exists(dllPath))
            {
                Console.WriteLine("❌ 错误：同目录未找到 PcControlCenter.dll");
                Console.WriteLine($"   当前目录：{currentDir}");
                Console.WriteLine("\n📋 使用步骤：");
                Console.WriteLine("  1. 从小米电脑管家安装目录复制 PcControlCenter.dll 到本程序所在目录");
                Console.WriteLine("  2. 重新运行本程序");
                Console.WriteLine("  3. 程序会生成修改后的 DLL 和备份");
                Console.WriteLine("  4. 手动将修改后的 DLL 替换到原安装位置\n");
                throw new FileNotFoundException($"找不到 DLL 文件：{dllPath}");
            }

            Console.WriteLine($"✓ 找到 DLL：{dllPath}\n");

            // 步骤 2：备份原始 DLL
            Console.WriteLine("=== 步骤 1：备份原始 DLL ===");
            string backupPath = Path.Combine(currentDir, "PcControlCenter.dll.bak");
            int backupIndex = 1;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(currentDir, $"PcControlCenter_bak_{backupIndex}.dll");
                backupIndex++;
            }

            File.Copy(dllPath, backupPath, overwrite: false);
            Console.WriteLine($"✓ 原始 DLL 已备份：{Path.GetFileName(backupPath)}\n");

            // 步骤 3：加载并修改 DLL
            Console.WriteLine("=== 步骤 2：应用摄像头补丁 ===");
            var module = ModuleDefMD.Load(dllPath);

            var synergyType = FindType(module, "PcControlCenter.Services.UI.MainView.Instances.SynergyUIService");

            // 屏蔽 Toast 通知
            try
            {
                var showCameraToastMethod = FindMethod(synergyType, "ShowCameraToast");
                Console.WriteLine($"  [1] 屏蔽 ShowCameraToast");
                ModifyCameraToastMethod(showCameraToastMethod);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ ShowCameraToast: {ex.Message}");
            }

            // 屏蔽关闭 Toast
            try
            {
                var closeCameraToastMethod = FindMethod(synergyType, "CloseCameraToast");
                Console.WriteLine($"  [2] 屏蔽 CloseCameraToast");
                ModifyCameraToastMethod(closeCameraToastMethod);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ CloseCameraToast: {ex.Message}");
            }

            // 屏蔽合并确认对话框
            try
            {
                var onShowCombinedPromptMethod = FindMethod(synergyType, "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnShowCombinedPrompt");
                Console.WriteLine($"  [3] 屏蔽 OnShowCombinedPrompt");
                ModifyCameraToastMethod(onShowCombinedPromptMethod);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ OnShowCombinedPrompt: {ex.Message}");
            }

            // 屏蔽关闭合并确认对话框
            try
            {
                var onCloseCombinedPromptMethod = FindMethod(synergyType, "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnCloseCombinedPrompt");
                Console.WriteLine($"  [4] 屏蔽 OnCloseCombinedPrompt");
                ModifyCameraToastMethod(onCloseCombinedPromptMethod);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ OnCloseCombinedPrompt: {ex.Message}");
            }

            Console.WriteLine("✓ 补丁应用完成\n");

            // 步骤 4：生成修改后的 DLL
            Console.WriteLine("=== 步骤 3：生成修改后的 DLL ===");
            string outputPath = Path.Combine(currentDir, "PcControlCenter_PATCHED.dll");
            int outputIndex = 1;
            while (File.Exists(outputPath))
            {
                outputPath = Path.Combine(currentDir, $"PcControlCenter_PATCHED_{outputIndex}.dll");
                outputIndex++;
            }

            module.Write(outputPath);
            var outputInfo = new FileInfo(outputPath);
            Console.WriteLine($"✓ 修改后的 DLL 已生成：{Path.GetFileName(outputPath)}");
            Console.WriteLine($"  文件大小：{outputInfo.Length / 1024} KB\n");

            // 步骤 5：提示用户
            Console.WriteLine("╔════════════════════════════════════════════════════╗");
            Console.WriteLine("║                   ✓ 修补完成！                    ║");
            Console.WriteLine("╚════════════════════════════════════════════════════╝\n");

            Console.WriteLine("📁 生成的文件（都在当前目录）：");
            Console.WriteLine($"  • {Path.GetFileName(backupPath)} - 原始 DLL 备份");
            Console.WriteLine($"  • {Path.GetFileName(outputPath)} - 修改后的 DLL\n");

            Console.WriteLine("🔧 后续步骤：");
            Console.WriteLine("  1. 关闭小米电脑管家应用");
            Console.WriteLine("  2. 停止小米相关后台服务（可选，但推荐）");
            Console.WriteLine("  3. 将修改后的 DLL 手动复制到：");
            Console.WriteLine("     C:\\Program Files\\MI\\XiaomiPCManager\\5.8.0.14\\PcControlCenter.dll");
            Console.WriteLine("     （覆盖原文件，系统会提示需要管理员权限）");
            Console.WriteLine("  4. 重启小米电脑管家\n");

            Console.WriteLine("⚠ 恢复方法：");
            Console.WriteLine($"  如需恢复原版 DLL，将 {Path.GetFileName(backupPath)} 复制回原位置即可\n");

            Console.WriteLine("按任意键退出...");
            Console.ReadLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ 操作失败：{ex.Message}");
            Console.WriteLine($"异常类型：{ex.GetType().Name}");
            Console.WriteLine("\n按任意键退出...");
            Console.ReadLine();
        }
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
        // 先尝试完全匹配
        var targetMethod = type.Methods.FirstOrDefault(m => m.Name == methodName);
        
        // 如果没找到，尝试模糊匹配（处理接口实现方法名，如 "ICameraCooperationWrapperUI.OnShowCombinedPrompt"）
        if (targetMethod == null && methodName.Contains("."))
        {
            targetMethod = type.Methods.FirstOrDefault(m => m.Name.EndsWith("." + methodName) || m.Name == methodName);
        }
        
        // 如果还是没找到，尝试只匹配方法名结尾部分
        if (targetMethod == null)
        {
            string lastPart = methodName.Split('.').Last();
            targetMethod = type.Methods.FirstOrDefault(m => m.Name.EndsWith("." + lastPart) || m.Name == lastPart);
        }
        
        if (targetMethod == null)
            throw new ArgumentException($"未找到方法：{methodName}（已尝试全名、接口名、简名）");

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
    /// 使用"删除后重建"策略而不是直接覆盖，以打破内存映射连接
    /// </summary>
    private static void WriteModuleWithRetry(ModuleDef module, string outputPath, int maxRetries = 5, int delayMs = 2000)
    {
        // 记录原始文件的修改时间
        DateTime originalModifyTime = DateTime.MinValue;
        bool isDirectReplace = false;
        if (File.Exists(outputPath))
        {
            var fileInfo = new FileInfo(outputPath);
            originalModifyTime = fileInfo.LastWriteTime;
            isDirectReplace = true;
            Console.WriteLine($"原文件修改时间：{originalModifyTime:yyyy-MM-dd HH:mm:ss}");
        }

        Exception? lastException = null;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                Console.WriteLine($"尝试写入 DLL（第 {i + 1}/{maxRetries} 次）...");
                
                if (isDirectReplace)
                {
                    // 对于直接替换，使用临时文件写入，然后替换原文件
                    string tempPath = outputPath + ".tmp";
                    try
                    {
                        // 先写入临时文件
                        module.Write(tempPath);
                        Console.WriteLine($"✓ 已写入临时文件：{tempPath}");
                        
                        // 删除原文件（打破内存映射）
                        if (File.Exists(outputPath))
                        {
                            File.Delete(outputPath);
                            Console.WriteLine($"✓ 已删除原文件");
                            System.Threading.Thread.Sleep(500);  // 短暂延迟确保删除完成
                        }
                        
                        // 重命名临时文件为目标文件
                        File.Move(tempPath, outputPath, overwrite: true);
                        Console.WriteLine($"✓ 已替换为新 DLL");
                        
                        // 验证文件是否真的被修改
                        if (File.Exists(outputPath))
                        {
                            var fileInfo = new FileInfo(outputPath);
                            DateTime newModifyTime = fileInfo.LastWriteTime;
                            
                            if (newModifyTime > originalModifyTime)
                            {
                                Console.WriteLine($"✓ DLL 已成功写入，新的修改时间：{newModifyTime:yyyy-MM-dd HH:mm:ss}");
                                return;
                            }
                            else
                            {
                                throw new IOException("DLL 文件写入失败：修改时间未更新");
                            }
                        }
                        else
                        {
                            throw new FileNotFoundException($"写入后文件不存在：{outputPath}");
                        }
                    }
                    finally
                    {
                        // 清理临时文件
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    }
                }
                else
                {
                    // 生成新文件，直接写入
                    module.Write(outputPath);
                    Console.WriteLine($"✓ DLL 已成功写入：{outputPath}");
                    return;
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

    // 检查文件锁定状态

    /// <summary>
    /// 检查哪些进程可能持有文件锁（基于尝试打开文件）
    /// </summary>
    private static List<string>? FindFileLocks(string filePath)
    {
        try
        {
            // 尝试以独占方式打开文件，测试文件是否被锁定
            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // 如果能打开，说明没有被锁定
                stream.Close();
                return null;  // 文件未被锁定
            }
        }
        catch (IOException)
        {
            // 文件被锁定，但 .NET 不能直接列举进程
            // 可以尝试通过系统调用，但这里只是返回提示
            try
            {
                // 尝试用更低的权限再检查一次
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    stream.Close();
                }
                return new List<string> { "文件被内存映射（system mapped）或防病毒软件扫描中" };
            }
            catch
            {
                return new List<string> { "文件被进程独占锁定（可能是 System、Services 或其他系统组件）" };
            }
        }
        catch
        {
            return null;
        }
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