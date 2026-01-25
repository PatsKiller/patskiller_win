using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// Scans Windows registry for installed J2534 devices
    /// </summary>
    public static class J2534DeviceScanner
    {
        private const string REGISTRY_PATH_64 = @"SOFTWARE\WOW6432Node\PassThruSupport.04.04";
        private const string REGISTRY_PATH_32 = @"SOFTWARE\PassThruSupport.04.04";

        public static List<J2534DeviceInfo> ScanForDevices()
        {
            var devices = new List<J2534DeviceInfo>();
            
            // Try 64-bit registry first, then 32-bit
            ScanRegistryPath(REGISTRY_PATH_64, devices);
            if (devices.Count == 0)
            {
                ScanRegistryPath(REGISTRY_PATH_32, devices);
            }
            
            return devices;
        }

        private static void ScanRegistryPath(string basePath, List<J2534DeviceInfo> devices)
        {
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(basePath);
                if (baseKey == null) return;

                foreach (var deviceName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var deviceKey = baseKey.OpenSubKey(deviceName);
                        if (deviceKey == null) continue;

                        var device = ReadDeviceInfo(deviceKey, deviceName);
                        if (device != null && !string.IsNullOrEmpty(device.FunctionLibrary))
                        {
                            devices.Add(device);
                        }
                    }
                    catch { /* Skip problematic device entries */ }
                }
            }
            catch { /* Registry access failed */ }
        }

        private static J2534DeviceInfo? ReadDeviceInfo(RegistryKey deviceKey, string deviceName)
        {
            var functionLibrary = deviceKey.GetValue("FunctionLibrary") as string;
            if (string.IsNullOrEmpty(functionLibrary)) return null;

            var vendor = deviceKey.GetValue("Vendor") as string ?? "Unknown";
            var name = deviceKey.GetValue("Name") as string ?? deviceName;
            
            // Check for CAN protocol support
            var canValue = deviceKey.GetValue("CAN");
            var iso15765Value = deviceKey.GetValue("ISO15765");
            bool supportsCan = (canValue != null && Convert.ToInt32(canValue) != 0) ||
                              (iso15765Value != null && Convert.ToInt32(iso15765Value) != 0);

            return new J2534DeviceInfo
            {
                Name = name,
                Vendor = vendor,
                FunctionLibrary = functionLibrary,
                SupportsCAN = supportsCan,
                DeviceType = DetectDeviceType(name, vendor)
            };
        }

        private static J2534DeviceType DetectDeviceType(string name, string vendor)
        {
            var combined = (name + " " + vendor).ToUpperInvariant();
            
            if (combined.Contains("VCM II") || combined.Contains("VCMII")) return J2534DeviceType.FordVCMII;
            if (combined.Contains("VCM III") || combined.Contains("VCMIII")) return J2534DeviceType.FordVCMIII;
            if (combined.Contains("MONGOOSE")) return J2534DeviceType.DrewTechMongoose;
            if (combined.Contains("CARDAQ")) return J2534DeviceType.DrewTechCarDAQ;
            if (combined.Contains("AUTEL")) return J2534DeviceType.Autel;
            if (combined.Contains("TOPDON")) return J2534DeviceType.Topdon;
            if (combined.Contains("VXDIAG")) return J2534DeviceType.VXDIAG;
            if (combined.Contains("OPENPORT")) return J2534DeviceType.OpenPort;
            
            return J2534DeviceType.Generic;
        }

        public static bool HasAnyDevice()
        {
            return ScanForDevices().Count > 0;
        }

        public static J2534DeviceInfo? GetFirstAvailableDevice()
        {
            var devices = ScanForDevices();
            return devices.Count > 0 ? devices[0] : null;
        }

        public static J2534DeviceInfo? FindDeviceByName(string name)
        {
            var devices = ScanForDevices();
            return devices.Find(d => d.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        public static List<J2534DeviceInfo> FindDevicesByType(J2534DeviceType type)
        {
            return ScanForDevices().FindAll(d => d.DeviceType == type);
        }
    }

    public class J2534DeviceInfo
    {
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public string FunctionLibrary { get; set; } = "";
        public bool SupportsCAN { get; set; }
        public J2534DeviceType DeviceType { get; set; }

        public override string ToString() => $"{Name} ({Vendor})";
    }

    public enum J2534DeviceType
    {
        Generic,
        FordVCMII,
        FordVCMIII,
        DrewTechMongoose,
        DrewTechCarDAQ,
        Autel,
        Topdon,
        VXDIAG,
        OpenPort
    }
}
