using System;
using System.Runtime.Versioning;

namespace PCManagerCompatCli;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return new CliApp().Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }
}
