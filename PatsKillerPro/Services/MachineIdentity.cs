using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Generates and manages machine-bound identity for license validation.
    /// Combines hardware fingerprint (CPU + Motherboard + BIOS) with a
    /// per-install Software Instance ID (SIID) for strict machine binding.
    /// </summary>
    public static class MachineIdentity
    {
        private const string APP_FOLDER = "PatsKillerPro";
        private const string SIID_FILE = ".siid";

        private static string? _cachedMachineId;
        private static string? _cachedSiid;
        private static string? _cachedCombinedId;

        /// <summary>
        /// Hardware-based machine fingerprint (CPU + Motherboard + BIOS hash).
        /// Stable across reinstalls on the same hardware.
        /// </summary>
        public static string MachineId
        {
            get
            {
                _cachedMachineId ??= GenerateMachineId();
                return _cachedMachineId;
            }
        }

        /// <summary>
        /// Software Instance ID - unique GUID generated per installation.
        /// Changes if the app data folder is wiped or on a fresh install.
        /// </summary>
        public static string SIID
        {
            get
            {
                _cachedSiid ??= GetOrCreateSiid();
                return _cachedSiid;
            }
        }

        /// <summary>
        /// Combined identity: "MachineId:SIID" for license server communication.
        /// </summary>
        public static string CombinedId
        {
            get
            {
                _cachedCombinedId ??= $"{MachineId}:{SIID}";
                return _cachedCombinedId;
            }
        }

        /// <summary>
        /// Short display-friendly machine identifier (first 16 chars of MachineId).
        /// </summary>
        public static string DisplayId => MachineId.Length > 16 ? MachineId[..16] : MachineId;

        /// <summary>
        /// Short display-friendly instance ID (first 16 chars of SIID).
        /// </summary>
        public static string DisplaySiid => SIID.Length > 16 ? SIID[..16] : SIID;

        private static string GetDataFolder()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                APP_FOLDER);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            return folder;
        }

        private static string GenerateMachineId()
        {
            try
            {
                var data = new StringBuilder();
                data.Append(Environment.MachineName);
                data.Append(GetWmiValue("Win32_Processor", "ProcessorId"));
                data.Append(GetWmiValue("Win32_BaseBoard", "SerialNumber"));
                data.Append(GetWmiValue("Win32_BIOS", "SerialNumber"));

                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(data.ToString()));
                // URL-safe base64, first 20 chars
                return Convert.ToBase64String(hash)
                    .Replace('+', '-').Replace('/', '_').TrimEnd('=')[..20];
            }
            catch (Exception ex)
            {
                Logger.Warn($"[MachineIdentity] WMI fallback: {ex.Message}");
                // Fallback: machine name only
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(
                    Environment.MachineName + Environment.UserName));
                return Convert.ToBase64String(hash)
                    .Replace('+', '-').Replace('/', '_').TrimEnd('=')[..20];
            }
        }

        private static string GetOrCreateSiid()
        {
            var siidPath = Path.Combine(GetDataFolder(), SIID_FILE);
            try
            {
                if (File.Exists(siidPath))
                {
                    var existing = File.ReadAllText(siidPath).Trim();
                    if (!string.IsNullOrWhiteSpace(existing) && existing.Length >= 16)
                        return existing;
                }

                // Generate new SIID
                var siid = Guid.NewGuid().ToString("N"); // 32 hex chars
                File.WriteAllText(siidPath, siid);
                Logger.Info($"[MachineIdentity] New SIID generated");
                return siid;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[MachineIdentity] SIID file error: {ex.Message}");
                // Transient fallback - will persist on next successful write
                return Guid.NewGuid().ToString("N");
            }
        }

        private static string GetWmiValue(string wmiClass, string property)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT {property} FROM {wmiClass}");
                foreach (var obj in searcher.Get())
                {
                    var val = obj[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }
            }
            catch { /* WMI not available */ }
            return "";
        }
    }
}
