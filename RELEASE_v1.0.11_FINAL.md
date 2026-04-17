# PCManagerPatcher v1.0.11 - 最终版本

**发布日期**：2026年4月17日  
**版本状态**：✅ FINAL（最终版）  
**GitHub**：https://github.com/oralvi/pcmanagerpatcher

---

## 🎉 项目成果

成功开发并验证了一款**小米电脑管家摄像头弹窗屏蔽工具**，通过 IL 级别的代码修改，彻底消除了烦人的摄像头确认对话框。

**测试结果**：✅ **问题彻底解决** - 摄像头弹窗完全消失

---

## 📋 技术细节

### 屏蔽方法（共 16 个）

#### UI 通知层 (2个)
- `ShowCameraToast` - 显示摄像头通知提示  
- `CloseCameraToast` - 关闭通知提示

#### 对话框层 (2个)
- `OnShowCombinedPrompt` - 显示合并确认对话框
- `OnCloseCombinedPrompt` - 关闭确认对话框

#### 摄像头协作回调层 (12个)
- `ExceptionCallback` - 异常处理回调
- `FirstUsedCallback` - 首次使用回调
- `OnAddLocalCamera` - 添加本地摄像头
- `OnRemoveDistributedCamera` - 移除分布式摄像头
- `OnClearDistributedCamera` - 清除分布式摄像头
- `OnShowCameraBall` - 显示摄像头球组件
- `OnUpdateCameraBallState` - 更新摄像头球状态
- `OnAddDistributedCamera` - 添加分布式摄像头
- `OnReSetSelectItem` - 重置选项
- `OnOpenCloseCamera` - 打开/关闭摄像头逻辑
- `UpdateCameraStatus` - 更新摄像头状态
- `AddCameraDevice` - 添加摄像头设备

### 关键技术

🔧 **IL 修改框架**：dnlib 4.4.0
- 精准定位目标方法（包括接口实现方法）
- 替换方法体为单个 `Ret` 指令（直接返回）
- 安全处理复杂控制流（异常处理器、分支指令）

🛡️ **安全策略**
- 在方法开始处插入 `Ret`（对于有异常处理器的方法）
- 完全清空指令体（对于简单方法）
- 自动备份原始 DLL
- 异常处理和错误恢复机制

📦 **发布策略**
- 单文件可执行文件（18.91 MB）
- 自包含，无需 .NET 运行时
- PublishTrimmed 优化（去除未使用代码）
- PublishReadyToRun 优化（预 JIT 编译）

---

## 🚀 使用指南

### 前提条件
- Windows 10/11 系统
- 小米电脑管家 v5.8.0.14（或相近版本）

### 使用步骤

1. **获取 DLL 文件**
   ```
   将小米电脑管家的 PcControlCenter.dll 复制出来
   位置通常在：C:\Program Files\MI\XiaomiPCManager\5.8.0.14\
   ```

2. **运行修补工具**
   ```powershell
   # 将 PcControlCenter.dll 放在 PCManagerPatcher.exe 同一目录
   .\PCManagerPatcher.exe
   ```

3. **处理生成的文件**
   ```
   ✓ 程序会生成：
   • PcControlCenter_PATCHED.dll - 修改后的 DLL
   • PcControlCenter.dll.bak - 原始 DLL 备份
   ```

4. **替换原文件**
   ```powershell
   # 停止小米电脑管家
   Stop-Process -Name "XiaomiPcManager" -Force
   
   # 替换 DLL（需要管理员权限）
   Copy-Item "PcControlCenter_PATCHED.dll" `
     -Destination "C:\Program Files\MI\XiaomiPCManager\5.8.0.14\PcControlCenter.dll" `
     -Force
   ```

5. **重启应用**
   ```
   启动小米电脑管家，观察摄像头弹窗是否消失
   ```

### 恢复原版
```powershell
Copy-Item "PcControlCenter.dll.bak" `
  -Destination "C:\Program Files\MI\XiaomiPCManager\5.8.0.14\PcControlCenter.dll" `
  -Force
```

---

## 📊 开发历程

| 版本 | 日期 | 屏蔽方法数 | 状态 |
|------|------|-----------|------|
| v1.0.0-1.0.4 | 2月 | 基础设置 | ✅ |
| v1.0.5-1.0.9 | 3月 | 系统级尝试 | ⚠️ |
| v1.0.10 | 4月17早 | 4 个方法 | ❌ 不足 |
| v1.0.11 | 4月17晚 | 16 个方法 | ✅ **FINAL** |

---

## 🔍 技术亮点

### 问题分析
- ✅ 使用 dnlib 进行深度 DLL 分析
- ✅ 识别 26 个摄像头相关方法
- ✅ 通过字符串常量分析找出关键逻辑

### 解决方案演进
- ❌ 虚拟摄像头 → 无效
- ❌ 配置文件修改 → 云端同步覆盖
- ✅ IL 修改 → 彻底解决

### 技术挑战 & 解决
```
问题：ModuleWriterException （控制流被破坏）
原因：简单清空有分支指令的方法体
解决：
  1. 检测异常处理器
  2. 保守策略：插入 Ret（保留原指令）
  3. 激进策略：清空所有（简单方法）
  4. 异常回退：清除异常处理器后重试
```

---

## 📁 项目结构

```
pcmanagerdll/
├── src/
│   ├── Program.cs (核心修补逻辑)
│   └── PCManagerPatcher.csproj
├── .github/
│   └── workflows/release.yml (CI/CD)
├── artifacts/
│   └── PCManagerPatcher.exe (最终可执行文件)
├── DLL_ANALYSIS_2026-04-17.md (DLL 分析报告)
└── README.md
```

---

## 💡 关键代码片段

### 安全的方法屏蔽
```csharp
private static void ModifyCameraToastMethod(MethodDef method)
{
    var methodBody = method.Body;
    bool hasExceptionHandlers = methodBody.ExceptionHandlers.Count > 0;
    
    if (hasExceptionHandlers)
    {
        // 保守策略：插入 Ret 保留原指令
        methodBody.Instructions.Insert(0, Instruction.Create(OpCodes.Ret));
    }
    else
    {
        // 激进策略：完全替换
        methodBody.Instructions.Clear();
        methodBody.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }
}
```

### 接口方法定位
```csharp
// 处理形如 "MiSmartShareClrWrapper.ICameraCooperationWrapperUI.OnShowCombinedPrompt" 的嵌套名称
var method = synergyType.Methods
    .FirstOrDefault(m => m.FullName.EndsWith(methodNamePart));
```

---

## ✅ 测试验证

**最终测试**（2026-04-17）
- ✅ 小米电脑管家启动
- ✅ 连接摄像头触发
- ✅ 无摄像头确认对话框
- ✅ 应用正常运行
- ✅ 其他功能未受影响

---

## 📝 许可证

MIT License - 可自由使用、修改和分发

---

## 🙏 致谢

感谢 dnlib 库的强大功能，使得精准的 IL 级别代码修改成为可能。

---

**项目完成**：2026年4月17日  
**最终状态**：✅ Production Ready
