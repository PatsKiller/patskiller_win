using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace PatsKillerPro.Utils
{
    /// <summary>
    /// Provides unique machine identification for license binding.
    ///
    /// Two identifiers:
    ///   MachineId  – SHA-256 of CPU + Motherboard + BIOS serials (survives OS reinstall)
    ///   SIID       – random GUID stored on disk at first launch   (survives HW change)
    ///   CombinedId – "MachineId:SIID" sent to server              (survives neither alone)
    ///
    /// Used by LicenseService, ProActivityLogger, and LicenseActivationForm.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class MachineIdentity
    {
        private const string APP_FOLDER = "PatsKillerPro";
        private const string SIID_FILE = ".siid";

        private static string? _machineId;
        private static string? _siid;
        private static string? _combinedId;

        // ───────────────────── Public Properties ──────────────────────

        /// <summary>
        /// Hardware fingerprint: SHA-256(CPU ProcessorId + Motherboard Serial + BIOS Serial).
        /// First 20 chars of Base64 hash. Survives OS reinstall; changes on HW swap.
        /// </summary>
        public static string MachineId => _machineId ??= GenerateMachineId();

        /// <summary>
        /// Software Instance ID: 16-char hex GUID generated once and stored in
        /// %LocalAppData%\PatsKillerPro\.siid.  Survives HW change; lost on reinstall.
        /// </summary>
        public static string SIID => _siid ??= GetOrCreateSIID();

        /// <summary>
        /// Combined identifier: "MachineId:SIID".
        /// Both halves must match for license validation to pass.
        /// </summary>
        public static string CombinedId => _combinedId ??= $"{MachineId}:{SIID}";

        /// <summary>
        /// Friendly machine name shown in admin console / license dialogs.
        /// </summary>
        public static string MachineName => Environment.MachineName;

        // ───────────────────── Machine ID (hardware) ─────────────────

        private static string GenerateMachineId()
        {
            try
            {
                var data = new StringBuilder();
                data.Append(GetWmiValue("Win32_Processor", "ProcessorId"));
                data.Append(GetWmiValue("Win32_BaseBoard", "SerialNumber"));
                data.Append(GetWmiValue("Win32_BIOS", "SerialNumber"));

                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(data.ToString()));
                return Convert.ToBase64String(hash)[..20]; // 20-char fingerprint
            }
            catch
            {
                // Fallback: machine name + user hash
                using var sha = SHA256.Create();
                var fallback = Environment.MachineName + Environment.UserName;
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fallback));
                return Convert.ToBase64String(hash)[..20];
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
                    return obj[property]?.ToString() ?? "";
                }
            }
            catch { }
            return "";
        }

        // ───────────────────── SIID (software instance) ──────────────

        private static string GetOrCreateSIID()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                APP_FOLDER);
            Directory.CreateDirectory(folder);

            var siidPath = Path.Combine(folder, SIID_FILE);

            // Read existing
            if (File.Exists(siidPath))
            {
                var existing = File.ReadAllText(siidPath).Trim();
                if (existing.Length >= 16)
                    return existing[..16];
            }

            // Generate new 16-char hex GUID
            var siid = Guid.NewGuid().ToString("N")[..16];
            File.WriteAllText(siidPath, siid);
            return siid;
        }
    }
}
