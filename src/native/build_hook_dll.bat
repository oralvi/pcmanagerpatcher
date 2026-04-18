@echo off
setlocal enabledelayedexpansion

where cl.exe >nul 2>nul
if errorlevel 1 (
    set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
    if not exist "%VSWHERE%" (
        echo Build FAILED: vswhere.exe not found
        exit /b 1
    )

    for /f "usebackq tokens=*" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALLDIR=%%I"
    if "%VSINSTALLDIR%"=="" (
        echo Build FAILED: Visual Studio with C++ tools not found
        exit /b 1
    )

    call "%VSINSTALLDIR%\VC\Auxiliary\Build\vcvars64.bat"
    if errorlevel 1 (
        echo Build FAILED: vcvars64.bat initialization failed
        exit /b 1
    )

    if not "%VCToolsInstallDir%"=="" (
        set "PATH=%VCToolsInstallDir%bin\Hostx64\x64;%PATH%"
    )
)

set SOURCE_DIR=%~dp0
set OUTPUT_PATH=%~1
if "%OUTPUT_PATH%"=="" set OUTPUT_PATH=%SOURCE_DIR%..\bin\Debug\net8.0-windows\win-x64\hook.dll
set DETOURS_INCLUDE=%SOURCE_DIR%..\thirdparty\detours
set DETOURS_LIB=%SOURCE_DIR%..\thirdparty\detours\lib
for %%I in ("%OUTPUT_PATH%") do (
    set OUTPUT_DIR=%%~dpI
    set OUTPUT_FILE=%%~fI
)

if not exist "!OUTPUT_DIR!" mkdir "!OUTPUT_DIR!"

cd /d "!SOURCE_DIR!"

echo Compiling hook.dll...

cl.exe /nologo /LD /O2 /MD /DUNICODE /D_UNICODE /I"!DETOURS_INCLUDE!" hookdll.c /link /NOLOGO /LIBPATH:"!DETOURS_LIB!" detours.lib kernel32.lib oleaut32.lib ole32.lib /DYNAMICBASE /NXCOMPAT /OPT:REF /OPT:ICF /OUT:"!OUTPUT_FILE!"

if !ERRORLEVEL! NEQ 0 (
    echo Build FAILED with error code !ERRORLEVEL!
    exit /b !ERRORLEVEL!
)

echo.
echo Build SUCCESS: !OUTPUT_FILE!
for %%A in ("!OUTPUT_FILE!") do echo Size: %%~zA bytes

endlocal
