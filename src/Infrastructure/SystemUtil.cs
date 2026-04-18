using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

namespace PCManagerCompatCli.Infrastructure;

internal sealed record CommandResult(int ExitCode, string StdOut, string StdErr)
{
    public bool IsSuccess => ExitCode == 0;
}

[SupportedOSPlatform("windows")]
internal static class SystemUtil
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void RelaunchAsAdmin(IEnumerable<string> args)
    {
        var exePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("无法解析当前程序路径");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Directory.GetCurrentDirectory(),
            Arguments = string.Join(" ", args.Select(QuoteWindowsArgument))
        };

        Process.Start(psi);
    }

    public static CommandResult RunProcess(
        string fileName,
        IEnumerable<string>? arguments = null,
        string? workingDirectory = null,
        bool captureOutput = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        if (captureOutput)
        {
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
        }

        if (arguments != null)
        {
            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"无法启动进程: {fileName}");

        if (!captureOutput)
        {
            process.WaitForExit();
            return new CommandResult(process.ExitCode, string.Empty, string.Empty);
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new CommandResult(process.ExitCode, stdout.Trim(), stderr.Trim());
    }

    public static CommandResult RunPowerShell(string script)
    {
        return RunProcess(
            "powershell",
            new[]
            {
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                script
            });
    }

    public static string PSQuote(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

    public static IReadOnlyList<string> SplitCommandLine(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in raw)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    private static string QuoteWindowsArgument(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            return "\"\"";
        }

        if (!arg.Any(char.IsWhiteSpace) && !arg.Contains('"'))
        {
            return arg;
        }

        return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
