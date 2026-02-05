using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace PatsKillerPro.Utils
{
    /// <summary>
    /// Generates and caches hardware + software identifiers for license machine-binding.
    ///
    /// Two independent IDs are produced:
    ///   • MachineId  – SHA-256 of CPU ProcessorId + Motherboard Serial + BIOS Serial (via WMI).
    ///                  Survives OS re-install; changes with hardware swap.
    ///   • SIID       – Random GUID written to %LocalAppData%\PatsKillerPro\.siid on first run.
    ///                  Survives hardware swap; changes with OS re-install / app re-install.
    ///   • CombinedId – "{MachineId}:{SIID}" sent to server for the strictest binding.
    ///
    /// Both are computed once at process start and cached for the lifetime of the application.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class MachineIdentity
    {
        private static readonly Lazy<string> _machineId = new(ComputeMachineId);
        private static readonly Lazy<string> _siid = new(GetOrCreateSIID);

        private const string APP_FOLDER = "PatsKillerPro";
        private const string SIID_FILE = ".siid";

        // ──────────────────────────────── Public API ────────────────────────────────

        /// <summary>
        /// Hardware fingerprint (CPU + Motherboard + BIOS).  20 chars, Base64.
        /// Stable across OS re-installs on the same physical machine.
        /// </summary>
        public static string MachineId => _machineId.Value;

        /// <summary>
        /// Software Instance ID (random GUID persisted to disk on first launch).  16 chars hex.
        /// Unique per install – survives hardware changes.
        /// </summary>
        public static string SIID => _siid.Value;

        /// <summary>
        /// "{MachineId}:{SIID}" – the value sent to the license server.
        /// If either component changes, the activation slot must be re-claimed.
        /// </summary>
        public static string CombinedId => $"{MachineId}:{SIID}";

        // ──────────────────────── Machine ID (Hardware) ─────────────────────────────

        private static string ComputeMachineId()
        {
            try
            {
                var data = new StringBuilder();

                // WMI hardware identifiers – these survive OS re-installs
                data.Append(GetWmiValue("Win32_Processor", "ProcessorId"));
                data.Append(GetWmiValue("Win32_BaseBoard", "SerialNumber"));
                data.Append(GetWmiValue("Win32_BIOS", "SerialNumber"));

                // Only fall back if ALL queries returned empty
                if (data.Length == 0)
                {
                    Logger.Warning("[MachineIdentity] WMI returned no hardware data, using fallback");
                    data.Append(Environment.MachineName);
                    data.Append(Environment.ProcessorCount);
                }

                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(data.ToString()));
                var id = Convert.ToBase64String(hash)[..20]; // 20-char fingerprint

                Logger.Info($"[MachineIdentity] MachineId generated: {id[..6]}...");
                return id;
            }
            catch (Exception ex)
            {
                Logger.Error($"[MachineIdentity] MachineId generation failed: {ex.Message}");
                // Absolute fallback – deterministic but weak
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Environment.MachineName + Environment.UserName));
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
                    var val = obj[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(val) && val != "To Be Filled By O.E.M.")
                        return val;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"[MachineIdentity] WMI {wmiClass}.{property} failed: {ex.Message}");
            }
            return "";
        }

        // ────────────────────── SIID (Software Instance) ────────────────────────────

        private static string GetOrCreateSIID()
        {
            try
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    APP_FOLDER);
                var siidPath = Path.Combine(folder, SIID_FILE);

                // Read existing SIID
                if (File.Exists(siidPath))
                {
                    var existing = File.ReadAllText(siidPath).Trim();
                    if (existing.Length >= 12 && existing.Length <= 40)
                    {
                        Logger.Info($"[MachineIdentity] SIID loaded: {existing[..6]}...");
                        return existing;
                    }
                    Logger.Warning($"[MachineIdentity] SIID file corrupt ({existing.Length} chars), regenerating");
                }

                // Generate new SIID
                Directory.CreateDirectory(folder);
                var siid = Guid.NewGuid().ToString("N")[..16]; // 16-char hex: "a1b2c3d4e5f6g7h8"
                File.WriteAllText(siidPath, siid);

                Logger.Info($"[MachineIdentity] SIID created: {siid[..6]}...");
                return siid;
            }
            catch (Exception ex)
            {
                Logger.Error($"[MachineIdentity] SIID creation failed: {ex.Message}");
                // Deterministic fallback tied to machine name + user – not ideal but functional
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(
                    $"SIID_FALLBACK_{Environment.MachineName}_{Environment.UserName}"));
                return BitConverter.ToString(hash).Replace("-", "")[..16].ToLowerInvariant();
            }
        }

        // ──────────────────────── Diagnostic Helpers ────────────────────────────────

        /// <summary>
        /// Returns a summary string for display in the UI (truncated for security).
        /// Example: "Machine: aBcDeF...  SIID: a1b2c3..."
        /// </summary>
        public static string GetDisplaySummary()
        {
            var mid = MachineId.Length > 6 ? MachineId[..6] + "..." : MachineId;
            var sid = SIID.Length > 6 ? SIID[..6] + "..." : SIID;
            return $"Machine: {mid}  SIID: {sid}";
        }

        /// <summary>
        /// Returns full diagnostic info (for log files, NOT for UI display).
        /// </summary>
        public static string GetDiagnosticInfo()
        {
            return $"MachineId={MachineId} | SIID={SIID} | Combined={CombinedId} | Host={Environment.MachineName}";
        }
    }
}
