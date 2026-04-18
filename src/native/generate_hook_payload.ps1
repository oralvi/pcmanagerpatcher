param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$bytes = [System.IO.File]::ReadAllBytes($InputPath)
$base64 = [System.Convert]::ToBase64String($bytes)
$content = @"
namespace PCManagerCompatCli.NativePayload;

internal static class HookDllPayload
{
    internal const string Base64 = "$base64";
}
"@

$dir = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($dir)) {
    [System.IO.Directory]::CreateDirectory($dir) | Out-Null
}

[System.IO.File]::WriteAllText($OutputPath, $content, [System.Text.Encoding]::UTF8)
