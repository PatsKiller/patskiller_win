using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PatsKillerPro.Utils;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// Manages J2534 device discovery and connection
    /// </summary>
    public class J2534DeviceManager : IDisposable
    {
        private readonly List<J2534DeviceInfo> _devices = new();
        private bool _disposed = false;

        /// <summary>
        /// Scans Windows registry for installed J2534 devices
        /// </summary>
        public void ScanForDevices()
        {
            _devices.Clear();
            Logger.Info("Scanning registry for J2534 devices...");

            // J2534 v2 registry path (04.04)
            ScanRegistryPath(@"SOFTWARE\PassThruSupport.04.04");
            
            // Also check WOW6432Node for 32-bit drivers on 64-bit Windows
            ScanRegistryPath(@"SOFTWARE\WOW6432Node\PassThruSupport.04.04");

            Logger.Info($"Found {_devices.Count} J2534 devices");
        }

        private void ScanRegistryPath(string basePath)
        {
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(basePath);
                if (baseKey == null) return;

                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var deviceKey = baseKey.OpenSubKey(subKeyName);
                        if (deviceKey == null) continue;

                        var name = deviceKey.GetValue("Name")?.ToString();
                        var vendor = deviceKey.GetValue("Vendor")?.ToString();
                        var functionLibrary = deviceKey.GetValue("FunctionLibrary")?.ToString();

                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(functionLibrary))
                            continue;

                        // Check if DLL exists
                        if (!System.IO.File.Exists(functionLibrary))
                        {
                            Logger.Warning($"J2534 DLL not found for {name}: {functionLibrary}");
                            continue;
                        }

                        // Check for duplicate
                        if (_devices.Exists(d => d.Name == name))
                            continue;

                        var deviceInfo = new J2534DeviceInfo
                        {
                            Name = name,
                            Vendor = vendor ?? "Unknown",
                            FunctionLibrary = functionLibrary,
                            RegistryPath = $"{basePath}\\{subKeyName}"
                        };

                        // Read optional fields
                        deviceInfo.ConfigApplication = deviceKey.GetValue("ConfigApplication")?.ToString();
                        
                        var protocols = deviceKey.GetValue("ProtocolsSupported");
                        if (protocols is string[] protoArray)
                        {
                            deviceInfo.SupportedProtocols = protoArray;
                        }

                        _devices.Add(deviceInfo);
                        Logger.Info($"Found J2534 device: {name} ({vendor})");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error reading device key {subKeyName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error scanning registry path {basePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets list of detected device names
        /// </summary>
        public List<string> GetDeviceNames()
        {
            var names = new List<string>();
            foreach (var device in _devices)
            {
                names.Add(device.Name);
            }
            return names;
        }

        /// <summary>
        /// Gets device info by name
        /// </summary>
        public J2534DeviceInfo? GetDeviceInfo(string name)
        {
            return _devices.Find(d => d.Name == name);
        }

        /// <summary>
        /// Connects to a J2534 device by name
        /// </summary>
        public J2534Device ConnectToDevice(string name)
        {
            var deviceInfo = GetDeviceInfo(name);
            if (deviceInfo == null)
            {
                throw new J2534Exception($"Device not found: {name}");
            }

            var device = new J2534Device(deviceInfo);
            device.Connect();
            return device;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _devices.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Information about a J2534 device from registry
    /// </summary>
    public class J2534DeviceInfo
    {
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public string FunctionLibrary { get; set; } = "";
        public string RegistryPath { get; set; } = "";
        public string? ConfigApplication { get; set; }
        public string[]? SupportedProtocols { get; set; }
    }
}
