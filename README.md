# PCManagerPatcher

A precise .NET IL patcher for Xiaomi PC Manager (`PcControlCenter.dll`). This tool surgically removes unwanted UI notifications (specifically camera cooperation dialogs) while preserving all other functionality.

## Features

- **Precision Patching**: Targets specific methods in the camera cooperation service (`SynergyUIService.ShowCameraToast`)
- **Zero Side Effects**: Does not affect Bluetooth headsets, phone sync, or other notification systems
- **Safe Rollback**: Automatically backs up the original DLL before patching with timestamped filenames
- **Version Detection**: Automatically finds the latest installed version of Xiaomi PC Manager
- **Process Management**: Gracefully shuts down running Xiaomi PC Manager before patching

## Requirements

- Windows 10/11
- .NET 8.0 runtime or higher (built binaries are self-contained)
- Xiaomi PC Manager installed

## Installation

Download the latest release from [GitHub Releases](https://github.com/yourusername/PCManagerPatcher/releases) and extract the `PCManagerPatcher.exe`.

## Usage

```powershell
# Automatic: Auto-locate latest Xiaomi PC Manager version
.\PCManagerPatcher.exe

# Manual: Specify DLL path explicitly
.\PCManagerPatcher.exe -dll "C:\Program Files\MI\XiaomiPCManager\5.8.0.14\PcControlCenter.dll"
```

### Workflow

1. Closes running Xiaomi PC Manager process
2. Locates the latest version of `PcControlCenter.dll`
3. Creates a timestamped backup (e.g., `PcControlCenter.backup_20260416_153000.dll`)
4. Injects IL patch: makes `SynergyUIService.ShowCameraToast()` return immediately
5. Saves patched DLL (overwrites or generates `.patched` version)
6. Displays completion status and backup location

## How It Works

The patcher uses **dnlib** to perform IL-level bytecode modifications:

- Targets: `PcControlCenter.Services.UI.MainView.Instances.SynergyUIService::ShowCameraToast()`
- Modification: Replaces method body with a single `ret` instruction
- Effect: Camera cooperation notifications are silently suppressed
- Side Effects: None (other notification systems remain untouched)

## Safety

- **Backup**: Always creates a backup with timestamp (`backup_YYYYMMDD_HHMMSS.dll`)
- **Version Matching**: Detects and works with version `5.8.0.14` and attempts forward compatibility
- **No Cascading**: Only patches one specific entry point, avoiding unintended behavior changes
- **Easy Rollback**: Simply restore the backup file if something goes wrong

## Building from Source

### Prerequisites

- .NET SDK 8.0 or higher

### Compile

```bash
cd src
dotnet publish -c Release -r win-x64 --sc -p:PublishSingleFile=true -o ../artifacts
```

Output: `../artifacts/PCManagerPatcher.exe` (self-contained single file ~65 MB)

## Architecture

```
src/
├── Program.cs         # Main entry point, path resolution, backup logic
├── PCManagerPatcher.csproj  # Project file
└── [IL Modification Methods]
```

### Key Methods

- `Main()`: Entry point, process management, workflow orchestration
- `ResolveLatestVersionPath()`: Wildcard path resolution with version sorting
- `CreateBackup()`: Atomic backup with unique naming
- `ModifyCameraToastMethod()`: IL-level patch injection (clear instructions + Ret)
- `FindType()`, `FindMethod()`: Type/method resolution via dnlib
- `KillProcessByName()`: Graceful process termination

## Limitations

- Only works on Windows
- Requires administrative privileges (to manage processes and write to Program Files)
- Targets PC Manager 5.8.0.14+; earlier versions may have different method signatures
- Cannot selectively suppress individual notifications (all-or-nothing per method)

## Troubleshooting

### "Process found but could not locate version directory"
PC Manager may not be installed, or installed in non-standard location. Use manual `-dll` flag.

### "Cannot modify abstract method"
Method signature may have changed in a newer version. Check GitHub issues or open a new one.

### "DLL is locked by running process"
Disable auto-kill or manually shut down `XiaomiPCManager.exe` first.

## Legal Notice

This tool is provided **for educational and personal use only**. It modifies third-party software. Ensure you have the right to modify Xiaomi PC Manager according to your jurisdiction's laws and Xiaomi's terms of service. The authors assume no liability for misuse.

## Contributing

Issues and pull requests welcome. Please:
1. Include your PC Manager version
2. Describe expected vs. actual behavior
3. Attach relevant error logs

## License

MIT License - see [LICENSE](LICENSE) file

## Related

- [dnlib](https://github.com/0xd4d/dnlib) - .NET metadata and IL manipulation library
- [Mono.Cecil](https://github.com/jbevain/cecil) - Alternative IL library
