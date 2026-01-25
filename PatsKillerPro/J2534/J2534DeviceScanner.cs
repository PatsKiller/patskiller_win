using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// Scans Windows Registry to find installed J2534 pass-thru devices
    /// Supports: VCM II, VCM III, Mongoose, CarDAQ, Autel, Topdon, VXDIAG, etc.
    /// 
    /// Registry Paths:
    /// - 64-bit: HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\PassThruSupport.04.04
    /// - 32-bit: HKEY_LOCAL_MACHINE\SOFTWARE\PassThruSupport.04.04
    /// </summary>
    public static class J2534DeviceScanner
    {
        // Registry paths for J2534 v04.04 devices
        private const string REGISTRY_PATH_64 = @"SOFTWARE\WOW6432Node\PassThruSupport.04.04";
        private const string REGISTRY_PATH_32 = @"SOFTWARE\PassThruSupport.04.04";

        /// <summary>
        /// Scan for all installed J2534 devices
        /// </summary>
        public static List<J2534DeviceInfo> ScanForDevices()
        {
            var devices = new List<J2534DeviceInfo>();

            // Try 64-bit registry first (most common on modern systems)
            ScanRegistryPath(REGISTRY_PATH_64, devices);

            // Also check 32-bit registry
            ScanRegistryPath(REGISTRY_PATH_32, devices);

            // Remove duplicates (same DLL path)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            devices.RemoveAll(d => !seen.Add(d.FunctionLibrary));

            return devices;
        }

        private static void ScanRegistryPath(string registryPath, List<J2534DeviceInfo> devices)
        {
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(registryPath);
                if (baseKey == null) return;

                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var deviceKey = baseKey.OpenSubKey(subKeyName);
                        if (deviceKey == null) continue;

                        var device = ReadDeviceInfo(deviceKey, subKeyName);
                        if (device != null && !string.IsNullOrEmpty(device.FunctionLibrary))
                        {
                            // Check if DLL exists
                            device.IsAvailable = System.IO.File.Exists(device.FunctionLibrary);
                            devices.Add(device);
                        }
                    }
                    catch
                    {
                        // Skip devices that can't be read
                    }
                }
            }
            catch
            {
                // Registry path doesn't exist or access denied
            }
        }

        private static J2534DeviceInfo? ReadDeviceInfo(RegistryKey deviceKey, string keyName)
        {
            var name = deviceKey.GetValue("Name") as string;
            var vendor = deviceKey.GetValue("Vendor") as string;
            var functionLibrary = deviceKey.GetValue("FunctionLibrary") as string;
            var configApp = deviceKey.GetValue("ConfigApplication") as string;

            if (string.IsNullOrEmpty(functionLibrary))
                return null;

            // Read supported protocols
            var protocols = new List<string>();
            if (IsProtocolSupported(deviceKey, "CAN")) protocols.Add("CAN");
            if (IsProtocolSupported(deviceKey, "ISO15765")) protocols.Add("ISO15765");
            if (IsProtocolSupported(deviceKey, "ISO9141")) protocols.Add("ISO9141");
            if (IsProtocolSupported(deviceKey, "ISO14230")) protocols.Add("ISO14230");
            if (IsProtocolSupported(deviceKey, "J1850VPW")) protocols.Add("J1850VPW");
            if (IsProtocolSupported(deviceKey, "J1850PWM")) protocols.Add("J1850PWM");

            return new J2534DeviceInfo
            {
                Name = name ?? keyName,
                Vendor = vendor ?? "Unknown",
                FunctionLibrary = functionLibrary,
                ConfigApplication = configApp ?? "",
                RegistryKey = keyName,
                SupportedProtocols = protocols.ToArray(),
                DeviceType = DetectDeviceType(name ?? keyName, vendor ?? "")
            };
        }

        private static bool IsProtocolSupported(RegistryKey key, string protocol)
        {
            var value = key.GetValue(protocol);
            if (value is int intVal) return intVal == 1;
            if (value is uint uintVal) return uintVal == 1;
            return false;
        }

        /// <summary>
        /// Detect the device type from name/vendor for special handling
        /// </summary>
        private static J2534DeviceType DetectDeviceType(string name, string vendor)
        {
            var combined = $"{name} {vendor}".ToUpperInvariant();

            // Ford VCM devices
            if (combined.Contains("VCM II") || combined.Contains("VCMII"))
                return J2534DeviceType.FordVcmII;
            if (combined.Contains("VCM III") || combined.Contains("VCMIII") || combined.Contains("VCM 3"))
                return J2534DeviceType.FordVcmIII;

            // Drew Technologies
            if (combined.Contains("MONGOOSE"))
                return J2534DeviceType.DrewTechMongoose;
            if (combined.Contains("CARDAQ"))
                return J2534DeviceType.DrewTechCarDaq;

            // Autel
            if (combined.Contains("AUTEL"))
                return J2534DeviceType.Autel;

            // Topdon
            if (combined.Contains("TOPDON"))
                return J2534DeviceType.Topdon;

            // VXDIAG
            if (combined.Contains("VXDIAG") || combined.Contains("VCX"))
                return J2534DeviceType.VxDiag;

            // Bosch
            if (combined.Contains("BOSCH"))
                return J2534DeviceType.Bosch;

            // Tactrix
            if (combined.Contains("TACTRIX") || combined.Contains("OPENPORT"))
                return J2534DeviceType.TactrixOpenPort;

            // Generic
            return J2534DeviceType.Generic;
        }

        /// <summary>
        /// Check if any J2534 device is installed
        /// </summary>
        public static bool HasAnyDevice()
        {
            return ScanForDevices().Count > 0;
        }

        /// <summary>
        /// Get the first available (DLL exists) device
        /// </summary>
        public static J2534DeviceInfo? GetFirstAvailableDevice()
        {
            var devices = ScanForDevices();
            return devices.Find(d => d.IsAvailable);
        }

        /// <summary>
        /// Find device by name (partial match)
        /// </summary>
        public static J2534DeviceInfo? FindDeviceByName(string namePart)
        {
            var devices = ScanForDevices();
            return devices.Find(d => d.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Find devices by type
        /// </summary>
        public static List<J2534DeviceInfo> FindDevicesByType(J2534DeviceType type)
        {
            var devices = ScanForDevices();
            return devices.FindAll(d => d.DeviceType == type);
        }
    }

    /// <summary>
    /// Information about an installed J2534 device
    /// </summary>
    public class J2534DeviceInfo
    {
        /// <summary>Display name of the device</summary>
        public string Name { get; set; } = "";

        /// <summary>Vendor/manufacturer name</summary>
        public string Vendor { get; set; } = "";

        /// <summary>Full path to the J2534 DLL</summary>
        public string FunctionLibrary { get; set; } = "";

        /// <summary>Path to configuration application (optional)</summary>
        public string ConfigApplication { get; set; } = "";

        /// <summary>Registry key name where this device is registered</summary>
        public string RegistryKey { get; set; } = "";

        /// <summary>List of supported protocols (CAN, ISO15765, etc.)</summary>
        public string[] SupportedProtocols { get; set; } = Array.Empty<string>();

        /// <summary>Detected device type for special handling</summary>
        public J2534DeviceType DeviceType { get; set; } = J2534DeviceType.Generic;

        /// <summary>Whether the DLL file exists</summary>
        public bool IsAvailable { get; set; }

        /// <summary>Check if device supports ISO15765 (required for Ford PATS)</summary>
        public bool SupportsISO15765 => Array.Exists(SupportedProtocols, p => p == "ISO15765");

        /// <summary>Check if device supports CAN</summary>
        public bool SupportsCAN => Array.Exists(SupportedProtocols, p => p == "CAN");

        public override string ToString()
        {
            var status = IsAvailable ? "✓" : "✗";
            return $"[{status}] {Name} ({Vendor}) - {DeviceType}";
        }
    }

    /// <summary>
    /// Known J2534 device types for special handling
    /// </summary>
    public enum J2534DeviceType
    {
        Generic,
        FordVcmII,
        FordVcmIII,
        DrewTechMongoose,
        DrewTechCarDaq,
        Autel,
        Topdon,
        VxDiag,
        Bosch,
        TactrixOpenPort
    }
}
