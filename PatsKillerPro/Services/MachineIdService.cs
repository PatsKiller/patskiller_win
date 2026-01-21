using System;
using System.Collections.Generic;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Generates a unique machine identifier for license/session binding
    /// Uses combination of hardware + Windows IDs for uniqueness
    /// </summary>
    public static class MachineIdService
    {
        private static string? _cachedMachineId;
        
        /// <summary>
        /// Get unique machine identifier (cached)
        /// </summary>
        public static string GetMachineId()
        {
            if (!string.IsNullOrEmpty(_cachedMachineId))
                return _cachedMachineId;
            
            var components = new List<string>();
            
            // 1. CPU ID
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    components.Add(obj["ProcessorId"]?.ToString() ?? "");
                    break;
                }
            }
            catch { components.Add("CPU_UNKNOWN"); }
            
            // 2. Motherboard Serial
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (var obj in searcher.Get())
                {
                    components.Add(obj["SerialNumber"]?.ToString() ?? "");
                    break;
                }
            }
            catch { components.Add("MB_UNKNOWN"); }
            
            // 3. Windows Product ID
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                var productId = key?.GetValue("ProductId")?.ToString() ?? "";
                components.Add(productId);
            }
            catch { components.Add("WIN_UNKNOWN"); }
            
            // 4. Machine GUID
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var machineGuid = key?.GetValue("MachineGuid")?.ToString() ?? "";
                components.Add(machineGuid);
            }
            catch { components.Add("GUID_UNKNOWN"); }
            
            // Combine and hash
            var combined = string.Join("|", components);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
            
            _cachedMachineId = Convert.ToBase64String(hash)
                .Replace("+", "")
                .Replace("/", "")
                .Replace("=", "")
                .Substring(0, 32);
            
            return _cachedMachineId;
        }
        
        /// <summary>
        /// Get a friendly machine name
        /// </summary>
        public static string GetMachineName()
        {
            return Environment.MachineName;
        }
    }
}
