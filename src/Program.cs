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
            // 检查是否为分析模式
            if (args.Contains("--analyze") || args.Contains("-a"))
            {
                AnalyzeDLL(args);
                return;
            }

            Console.WriteLine("╔════════════════════════════════════════════════════╗");
            Console.WriteLine("║   小米电脑管家 - 摄像头弹窗屏蔽补丁工具              ║");
            Console.WriteLine("║   PCManager Patcher v1.0.11                        ║");
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

            // 屏蔽所有摄像头相关方法 (v1.0.11 - 全面屏蔽)
            var cameraMethodNames = new[]
            {
                // UI 通知 (2个)
                ("ShowCameraToast", "显示摄像头通知"),
                ("CloseCameraToast", "关闭摄像头通知"),
                
                // 合并对话框 (2个)
                ("MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnShowCombinedPrompt", "显示合并确认对话框"),
                ("MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnCloseCombinedPrompt", "关闭合并确认对话框"),
                
                // 摄像头协作回调 (12个)
                ("MiSmartShareClrWrapper.ICameraCooperationWrapperUI.ExceptionCallback", "异常回调"),
                ("MiSmartShareClrWrapper.ICameraCooperationWrapperUI.FirstUsedCallback", "首次使用回调"),
                ("MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnAddLocalCamera", "添加本地摄像头"),
                ("MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnRemoveDistributedCamera", "移除分布式摄像头"),
                ("MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnClearDistributedCamera", "清除分布式摄像头"),
                ("MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnShowCameraBall", "显示摄像头球"),
                ("MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnUpdateCameraBallState", "更新摄像头球状态"),
                ("MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnAddDistributedCamera", "添加分布式摄像头"),
                ("MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnReSetSelectItem", "重置选项"),
                ("MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnOpenCloseCamera", "打开/关闭摄像头"),
                ("UpdateCameraStatus", "更新摄像头状态"),
                ("AddCameraDevice", "添加摄像头设备")
            };

            int count = 1;
            foreach (var (methodName, description) in cameraMethodNames)
            {
                try
                {
                    var method = FindMethod(synergyType, methodName);
                    Console.WriteLine($"  [{count:D2}] 屏蔽 {description}");
                    ModifyCameraToastMethod(method);
                    count++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠ {methodName}: {ex.Message}");
                }
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

    // ===== 分析模式 =====
    
    private static void AnalyzeDLL(string[] args)
    {
        Console.WriteLine("╔════════════════════════════════════════════════════╗");
        Console.WriteLine("║   DLL 分析工具                                     ║");
        Console.WriteLine("║   PCManager DLL Analyzer                           ║");
        Console.WriteLine("╚════════════════════════════════════════════════════╝\n");

        string? dllPath = Environment.CurrentDirectory;
        
        // 从命令行参数获取 DLL 路径（如果有的话）
        var pathArg = args.FirstOrDefault(a => a != "--analyze" && a != "-a" && !a.StartsWith("-"));
        if (!string.IsNullOrEmpty(pathArg) && File.Exists(pathArg))
        {
            dllPath = pathArg;
        }
        else
        {
            // 尝试在当前目录找
            string localDll = Path.Combine(Environment.CurrentDirectory, "PcControlCenter.dll");
            if (File.Exists(localDll))
            {
                dllPath = localDll;
            }
            else
            {
                Console.WriteLine("❌ 未找到 DLL 文件");
                Console.WriteLine("使用方法：");
                Console.WriteLine("  PCManagerPatcher.exe --analyze [dll-path]");
                Console.WriteLine("  或将 PcControlCenter.dll 放在程序目录中");
                return;
            }
        }

        try
        {
            Console.WriteLine($"📂 分析 DLL：{Path.GetFileName(dllPath)}\n");
            var fileInfo = new FileInfo(dllPath);
            Console.WriteLine($"  大小：{fileInfo.Length / 1024} KB");
            Console.WriteLine($"  修改时间：{fileInfo.LastWriteTime}\n");

            var module = ModuleDefMD.Load(dllPath);
            Console.WriteLine($"✓ Assembly：{module.Name}");
            Console.WriteLine($"  版本：{module.Assembly?.Version}\n");

            // 分析 SynergyUIService
            AnalyzeSynergyUIService(module);

            // 分析 ICameraCooperationWrapperUI
            AnalyzeICameraCooperationWrapperUI(module);

            // 分析所有摄像头相关类型
            AnalyzeCameraTypes(module);

            // 分析配置和存储相关的线索
            if (args.Contains("--deep"))
            {
                AnalyzeConfigurationHints(module);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 分析失败：{ex.Message}");
        }
    }

    private static void AnalyzeSynergyUIService(ModuleDefMD module)
    {
        var synergyType = module.GetTypes().FirstOrDefault(t => t.Name == "SynergyUIService");
        if (synergyType == null)
        {
            Console.WriteLine("❌ 未找到 SynergyUIService");
            return;
        }

        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine("🔍 SynergyUIService");
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine($"  Namespace: {synergyType.Namespace}");
        Console.WriteLine($"  Full Name: {synergyType.FullName}");
        Console.WriteLine($"  Total Methods: {synergyType.Methods.Count}\n");

        Console.WriteLine("  摄像头相关方法：");
        var cameraMethods = synergyType.Methods.Where(m =>
            m.Name.Contains("Camera") || m.Name.Contains("Toast") ||
            m.Name.Contains("Prompt") || m.Name.Contains("Show") ||
            m.Name.Contains("Close"));

        if (!cameraMethods.Any())
        {
            Console.WriteLine("    (无)");
        }
        else
        {
            foreach (var method in cameraMethods)
            {
                var hasBody = method.Body?.Instructions.Count > 0;
                Console.WriteLine($"    • {method.Name} {(hasBody ? "" : "[ABSTRACT]")}");
            }
        }

        Console.WriteLine("\n  所有方法列表：");
        foreach (var method in synergyType.Methods.OrderBy(m => m.Name))
        {
            var mark = method.Name.Contains("Camera") || method.Name.Contains("Toast") ||
                      method.Name.Contains("Prompt") ? "🎯 " : "   ";
            Console.WriteLine($"    {mark}{method.Name}");
        }
        Console.WriteLine();
    }

    private static void AnalyzeICameraCooperationWrapperUI(ModuleDefMD module)
    {
        var iface = module.GetTypes().FirstOrDefault(t => t.Name == "ICameraCooperationWrapperUI");
        if (iface == null)
        {
            Console.WriteLine("❌ 未找到 ICameraCooperationWrapperUI");
            return;
        }

        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine("🔍 ICameraCooperationWrapperUI (Interface)");
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine($"  Namespace: {iface.Namespace}");
        Console.WriteLine($"  Methods:\n");

        foreach (var method in iface.Methods)
        {
            Console.WriteLine($"    • {method.Name}");
        }
        Console.WriteLine();

        // 查找实现这个接口的类型
        var implementingTypes = module.GetTypes().Where(t =>
            t.Interfaces.Any(i => i.Interface.Name == "ICameraCooperationWrapperUI"));

        if (implementingTypes.Any())
        {
            Console.WriteLine("  实现此接口的类型：");
            foreach (var type in implementingTypes)
            {
                Console.WriteLine($"    • {type.Name}");
            }
            Console.WriteLine();
        }
    }

    private static void AnalyzeCameraTypes(ModuleDefMD module)
    {
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine("🔍 所有包含 'Camera' 的类型");
        Console.WriteLine("═══════════════════════════════════════════════════");

        var cameraTypes = module.GetTypes().Where(t => t.Name.Contains("Camera")).ToList();
        if (!cameraTypes.Any())
        {
            Console.WriteLine("  (无)\n");
            return;
        }

        foreach (var type in cameraTypes.OrderBy(t => t.Name))
        {
            Console.WriteLine($"  • {type.Name} ({type.BaseType?.Name ?? "object"})");
            if (type.Methods.Any(m => m.Name.Contains("Camera") || m.Name.Contains("Show")))
            {
                foreach (var method in type.Methods.Where(m => m.Name.Contains("Camera") || m.Name.Contains("Show")))
                {
                    Console.WriteLine($"      - {method.Name}");
                }
            }
        }
        Console.WriteLine();
    }

    private static void AnalyzeConfigurationHints(ModuleDefMD module)
    {
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine("🔍 配置存储线索分析 (--deep)");
        Console.WriteLine("═══════════════════════════════════════════════════");

        var synergyType = module.GetTypes().FirstOrDefault(t => t.Name == "SynergyUIService");
        if (synergyType == null) return;

        // 查找所有字段（可能存储配置）
        Console.WriteLine("\n📦 SynergyUIService 字段（可能的配置和状态）：");
        foreach (var field in synergyType.Fields.OrderBy(f => f.Name))
        {
            var fieldType = field.FieldType.ToString();
            Console.WriteLine($"  • {field.Name} : {fieldType}");
        }

        // 查找所有属性
        Console.WriteLine("\n🔧 SynergyUIService 属性：");
        foreach (var property in synergyType.Properties.OrderBy(p => p.Name))
        {
            Console.WriteLine($"  • {property.Name}");
        }

        // 查找所有返回特定类型的方法
        Console.WriteLine("\n🎯 与状态/配置检查相关的方法签名：");
        foreach (var method in synergyType.Methods.Where(m => 
            m.ReturnType.ToString().Contains("Bool") || 
            m.Name.Contains("Get") || m.Name.Contains("Check") || 
            m.Name.Contains("Update") || m.Name.Contains("Load") || 
            m.Name.Contains("Save") || m.Name.Contains("Config")))
        {
            var returnType = method.ReturnType.ToString();
            Console.WriteLine($"  • {method.Name}() -> {returnType}");
        }

        Console.WriteLine("\n💡 查找字符串常量中的配置关键词：");
        var stringConstants = new List<string>();
        foreach (var method in synergyType.Methods.Where(m => m.Body != null))
        {
            foreach (var instr in method.Body.Instructions)
            {
                if (instr.OpCode == OpCodes.Ldstr)
                {
                    var ldstrValue = instr.Operand as string;
                    if (ldstrValue != null && 
                        (ldstrValue.Contains("camera", StringComparison.OrdinalIgnoreCase) ||
                         ldstrValue.Contains("device", StringComparison.OrdinalIgnoreCase) ||
                         ldstrValue.Contains("config", StringComparison.OrdinalIgnoreCase) ||
                         ldstrValue.Contains("save", StringComparison.OrdinalIgnoreCase) ||
                         ldstrValue.Contains("bind", StringComparison.OrdinalIgnoreCase) ||
                         ldstrValue.Contains("register", StringComparison.OrdinalIgnoreCase) ||
                         ldstrValue.Contains(".json", StringComparison.OrdinalIgnoreCase) ||
                         ldstrValue.Contains(".ini", StringComparison.OrdinalIgnoreCase) ||
                         ldstrValue.Contains(".db", StringComparison.OrdinalIgnoreCase)))
                    {
                        stringConstants.Add(ldstrValue);
                    }
                }
            }
        }

        if (stringConstants.Count > 0)
        {
            Console.WriteLine("  关键字符串常量：");
            foreach (var str in stringConstants.Distinct())
            {
                if (!string.IsNullOrWhiteSpace(str) && str.Length < 200)
                {
                    Console.WriteLine($"    • {str}");
                }
            }
        }
        else
        {
            Console.WriteLine("  (未找到关键字符串)");
        }
        Console.WriteLine();
    }
}