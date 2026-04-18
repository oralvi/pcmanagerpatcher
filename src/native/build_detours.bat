@echo off
setlocal enabledelayedexpansion

REM Detours 编译脚本（使用 MSBuild）

REM 设置 Visual Studio 开发环境
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"

REM 输出目录（工作区内）
set OUTPUT_DIR=d:\projects\reverse-analysis-workspace\projects\pcmanagerdll\src\thirdparty\detours\lib

if not exist "!OUTPUT_DIR!" mkdir "!OUTPUT_DIR!"

REM 编译 Detours x64 Release
echo Compiling Detours (x64 Release)...

cd /d d:\projects\Detours\vc

set MSBUILD="C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"

!MSBUILD! Detours.vcxproj /p:Configuration=ReleaseMD /p:Platform=x64 /m

if !ERRORLEVEL! EQU 0 (
    echo ✓ Detours 编译成功
    
    REM 复制生成的库文件到工作区
    copy "..\..\bin.x64\detours.lib" "!OUTPUT_DIR!" /Y >nul 2>&1
    if exist "!OUTPUT_DIR!\detours.lib" (
        echo ✓ 库文件已复制到: !OUTPUT_DIR!
        dir "!OUTPUT_DIR!" /B
    )
) else (
    echo ✗ Detours 编译失败
    exit /b 1
)

endlocal

