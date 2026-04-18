using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PCManagerCompatCli.Modules;

[SupportedOSPlatform("windows")]
internal sealed class RegistryProfileService
{
    private static readonly string[] DeviceServiceKeyPaths =
    {
        @"SOFTWARE\MI\MiDeviceService",
        @"SOFTWARE\WOW6432Node\MI\MiDeviceService"
    };

    private static readonly string[] ProfileValueNames =
    {
        "ProductModel",
        "Brand",
        "Manufacturer",
        "Series",
        "DeviceSubType"
    };

    internal sealed record RegistryKeySnapshot(string KeyPath, bool KeyExisted, Dictionary<string, string?> OriginalValues);

    internal sealed record ProfileSnapshot(IReadOnlyList<RegistryKeySnapshot> KeySnapshots);

    public ProfileSnapshot CaptureSnapshot()
    {
        var snapshots = new List<RegistryKeySnapshot>();

        foreach (var keyPath in DeviceServiceKeyPaths)
        {
            var originalValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
            var existed = key != null;

            foreach (var name in ProfileValueNames)
            {
                var value = key?.GetValue(name) as string;
                originalValues[name] = value;
            }

            snapshots.Add(new RegistryKeySnapshot(keyPath, existed, originalValues));
        }

        return new ProfileSnapshot(snapshots);
    }

    public void ApplyModelProfile(string productModel)
    {
        if (string.IsNullOrWhiteSpace(productModel))
        {
            throw new ArgumentException("机型名称不能为空", nameof(productModel));
        }

        foreach (var keyPath in DeviceServiceKeyPaths)
        {
            using var key = Registry.LocalMachine.CreateSubKey(keyPath, writable: true)
                ?? throw new InvalidOperationException($"无法写入 MiDeviceService 注册表项: {keyPath}");

            key.SetValue("ProductModel", productModel, RegistryValueKind.String);
            key.SetValue("Brand", "Xiaomi", RegistryValueKind.String);
            key.SetValue("Manufacturer", "Xiaomi", RegistryValueKind.String);
            key.SetValue("Series", "Mi", RegistryValueKind.String);
            key.SetValue("DeviceSubType", "Notebook", RegistryValueKind.String);
        }
    }

    public void RestoreSnapshot(ProfileSnapshot snapshot)
    {
        foreach (var keySnapshot in snapshot.KeySnapshots)
        {
            if (!keySnapshot.KeyExisted)
            {
                using var key = Registry.LocalMachine.CreateSubKey(keySnapshot.KeyPath, writable: true);
                if (key != null)
                {
                    foreach (var name in ProfileValueNames)
                    {
                        try
                        {
                            key.DeleteValue(name, throwOnMissingValue: false);
                        }
                        catch
                        {
                            // ignore cleanup failures for non-critical values
                        }
                    }
                }

                continue;
            }

            using var writableKey = Registry.LocalMachine.CreateSubKey(keySnapshot.KeyPath, writable: true)
                ?? throw new InvalidOperationException($"无法恢复 MiDeviceService 注册表项: {keySnapshot.KeyPath}");

            foreach (var (name, value) in keySnapshot.OriginalValues)
            {
                if (value == null)
                {
                    writableKey.DeleteValue(name, throwOnMissingValue: false);
                }
                else
                {
                    writableKey.SetValue(name, value, RegistryValueKind.String);
                }
            }
        }
    }
}
