using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Services
{
    // ═══════════════════════════════════════════════════════════════
    //  LicenseService  –  Singleton for hybrid license-key auth
    // ═══════════════════════════════════════════════════════════════

    public sealed class LicenseService
    {
        // ── Singleton ──────────────────────────────────────────────
        private static LicenseService? _instance;
        private static readonly object _lock = new();
        public static LicenseService Instance
        {
            get { if (_instance == null) lock (_lock) { _instance ??= new LicenseService(); } return _instance; }
        }

        // ── Constants ──────────────────────────────────────────────
        private const string APP_FOLDER = "PatsKillerPro";
        private const string LICENSE_FILE = "license.key";
        private const string LICENSE_API = "https://kmpnplpijuzzbftsjacx.supabase.co/functions/v1/bridge-license";
        private const string SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpanV6emJmdHNqYWN4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3NjQyNjc0MDUsImV4cCI6MjA3OTg0MzQwNX0.RLX0e1FAq7AlKIpVXhaw7J3ILY_yc0FJDoAzxQDy24E";
        private const string PRODUCT_NAME = "PatsKillerPro";
        private const string PRODUCT_VERSION = "2.1.0";
        private const int REVALIDATION_DAYS = 7;
        private const int GRACE_DAYS = 10;

        // ── Public state ───────────────────────────────────────────
        public bool IsLicensed => _current?.IsValid ?? false;
        public string? LicensedTo => _current?.CustomerName;
        public string? LicenseEmail => _current?.CustomerEmail;
        public LicenseType LicType => _current?.Type ?? LicenseType.None;
        public DateTime? ExpiresAt => _current?.ExpiresAt;
        public int MaxMachines => _current?.MaxMachines ?? 0;
        public int MachinesUsed => _current?.MachinesUsed ?? 0;

        // ── Events ─────────────────────────────────────────────────
        public event Action<string, string>? OnLogMessage;       // (type, message)
        public event Action<LicenseResult>? OnLicenseChanged;

        // ── Internals ──────────────────────────────────────────────
        private readonly HttpClient _http;
        private readonly string _dataFolder;
        private LicenseInfo? _current;

        private LicenseService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                APP_FOLDER);
            if (!Directory.Exists(_dataFolder))
                Directory.CreateDirectory(_dataFolder);
        }

        // ══════════════════════════════════════════════════════════
        //  PUBLIC API
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// Check existing license on startup (file cache → online revalidation).
        /// </summary>
        public async Task<LicenseResult> ValidateAsync()
        {
            // 1. Try cached file
            var cached = LoadLicenseFile();
            if (cached == null)
                return Fail("No license found. Please activate a license key.");

            // 2. Check basic validity
            if (cached.ExpiresAt.HasValue && cached.ExpiresAt.Value < DateTime.UtcNow)
            {
                _current = null;
                return Fail("License expired.");
            }

            // 3. Check machine binding
            if (!string.IsNullOrEmpty(cached.MachineId) &&
                cached.MachineId != MachineIdentity.MachineId &&
                cached.CombinedId != MachineIdentity.CombinedId)
            {
                _current = null;
                return Fail("License is bound to a different machine.");
            }

            // 4. Online revalidation check
            var daysSinceCheck = cached.LastValidatedAt.HasValue
                ? (DateTime.UtcNow - cached.LastValidatedAt.Value).TotalDays
                : 999;

            if (daysSinceCheck > REVALIDATION_DAYS)
            {
                // Try online revalidation
                var online = await RevalidateOnlineAsync(cached);
                if (online.IsValid)
                {
                    Emit("success", $"License validated online for {online.LicenseData?.CustomerName}");
                    return online;
                }

                // Grace period: 7-10 days without internet
                if (daysSinceCheck <= GRACE_DAYS)
                {
                    _current = cached;
                    int daysLeft = GRACE_DAYS - (int)daysSinceCheck;
                    Emit("warning", $"License grace period: {daysLeft} day(s) until internet required");
                    return new LicenseResult
                    {
                        IsValid = true,
                        Message = $"Grace period – connect within {daysLeft} day(s)",
                        LicenseData = cached,
                        GraceDaysRemaining = daysLeft
                    };
                }

                // Past grace period
                _current = null;
                return Fail("License revalidation required. Please connect to the internet.");
            }

            // 5. Still valid from cache
            _current = cached;
            Emit("success", $"Licensed to {cached.CustomerName}");
            return new LicenseResult { IsValid = true, Message = $"Licensed to {cached.CustomerName}", LicenseData = cached };
        }

        /// <summary>
        /// Activate a new license key.
        /// </summary>
        public async Task<LicenseResult> ActivateAsync(string licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
                return Fail("License key is empty.");

            // Normalise: strip spaces/dashes, uppercase
            licenseKey = licenseKey.Trim().ToUpperInvariant();

            try
            {
                Emit("info", "Activating license…");
                var result = await ActivateOnlineAsync(licenseKey);
                if (result.IsValid && result.LicenseData != null)
                {
                    SaveLicenseFile(result.LicenseData);
                    _current = result.LicenseData;
                    OnLicenseChanged?.Invoke(result);
                    Emit("success", $"License activated for {result.LicenseData.CustomerName}");
                }
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"[LicenseService] Activation error: {ex.Message}", ex);
                return Fail($"Activation error: {ex.Message}");
            }
        }

        /// <summary>
        /// Deactivate current license (free up machine slot).
        /// </summary>
        public async Task<bool> DeactivateAsync()
        {
            if (_current == null) return false;
            try
            {
                var body = JsonSerializer.Serialize(new
                {
                    action = "deactivate",
                    license_key = _current.LicenseKey,
                    machine_id = MachineIdentity.CombinedId
                });

                var req = new HttpRequestMessage(HttpMethod.Post, LICENSE_API)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                req.Headers.Add("apikey", SUPABASE_ANON_KEY);
                await _http.SendAsync(req);

                DeleteLicenseFile();
                _current = null;
                OnLicenseChanged?.Invoke(new LicenseResult { IsValid = false, Message = "License deactivated" });
                Emit("info", "License deactivated");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[LicenseService] Deactivate failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Periodic heartbeat (call on launch + every 4 hours).
        /// </summary>
        public async Task<bool> HeartbeatAsync()
        {
            if (_current == null) return false;
            try
            {
                var body = JsonSerializer.Serialize(new
                {
                    action = "heartbeat",
                    license_key = _current.LicenseKey,
                    machine_id = MachineIdentity.CombinedId,
                    version = PRODUCT_VERSION
                });

                var req = new HttpRequestMessage(HttpMethod.Post, LICENSE_API)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                req.Headers.Add("apikey", SUPABASE_ANON_KEY);
                var res = await _http.SendAsync(req);

                if (res.IsSuccessStatusCode)
                {
                    // Update last-validated timestamp
                    _current.LastValidatedAt = DateTime.UtcNow;
                    SaveLicenseFile(_current);
                    return true;
                }
            }
            catch { /* silent */ }
            return false;
        }

        /// <summary>
        /// Clear license state without server deactivation (for local logout).
        /// </summary>
        public void ClearLocal()
        {
            _current = null;
        }

        // ══════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ══════════════════════════════════════════════════════════

        private async Task<LicenseResult> ActivateOnlineAsync(string licenseKey)
        {
            var body = JsonSerializer.Serialize(new
            {
                action = "validate",
                license_key = licenseKey,
                machine_id = MachineIdentity.CombinedId,
                siid = MachineIdentity.SIID,
                product = PRODUCT_NAME,
                version = PRODUCT_VERSION,
                machine_name = Environment.MachineName
            });

            var req = new HttpRequestMessage(HttpMethod.Post, LICENSE_API)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("apikey", SUPABASE_ANON_KEY);

            try
            {
                var res = await _http.SendAsync(req);
                var json = await res.Content.ReadAsStringAsync();
                var api = JsonSerializer.Deserialize<LicenseApiResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (api?.Valid == true)
                {
                    var info = new LicenseInfo
                    {
                        LicenseKey = licenseKey,
                        CustomerName = api.LicensedTo ?? "Unknown",
                        CustomerEmail = api.Email,
                        Type = ParseType(api.LicenseType),
                        ExpiresAt = api.ExpiresAt,
                        MachineId = MachineIdentity.MachineId,
                        Siid = MachineIdentity.SIID,
                        CombinedId = MachineIdentity.CombinedId,
                        ActivatedAt = DateTime.UtcNow,
                        LastValidatedAt = DateTime.UtcNow,
                        MaxMachines = api.MaxMachines ?? 1,
                        MachinesUsed = api.MachinesUsed ?? 1
                    };
                    return new LicenseResult { IsValid = true, Message = $"Licensed to {info.CustomerName}", LicenseData = info };
                }

                var errorMsg = api?.Error switch
                {
                    "invalid_key" => "License key not found.",
                    "machine_limit" => $"Machine limit reached ({api?.MachinesUsed}/{api?.MaxMachines}).",
                    "expired" => $"License expired on {api?.ExpiresAt:d}.",
                    "disabled" => api?.Message ?? "License has been disabled.",
                    _ => api?.Message ?? "Invalid license key."
                };
                return Fail(errorMsg);
            }
            catch (HttpRequestException)
            {
                return Fail("Cannot reach license server — check your internet connection.");
            }
            catch (TaskCanceledException)
            {
                return Fail("License server timeout — please try again.");
            }
        }

        private async Task<LicenseResult> RevalidateOnlineAsync(LicenseInfo cached)
        {
            try
            {
                var result = await ActivateOnlineAsync(cached.LicenseKey);
                if (result.IsValid && result.LicenseData != null)
                {
                    SaveLicenseFile(result.LicenseData);
                    _current = result.LicenseData;
                }
                return result;
            }
            catch
            {
                return Fail("Online revalidation failed.");
            }
        }

        // ── File I/O ───────────────────────────────────────────────

        private string LicensePath => Path.Combine(_dataFolder, LICENSE_FILE);

        private LicenseInfo? LoadLicenseFile()
        {
            try
            {
                if (!File.Exists(LicensePath)) return null;
                var json = File.ReadAllText(LicensePath);
                var info = JsonSerializer.Deserialize<LicenseInfo>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // Verify integrity hash
                if (info != null && !string.IsNullOrEmpty(info.ValidationHash))
                {
                    var expected = ComputeHash(info);
                    if (info.ValidationHash != expected)
                    {
                        Logger.Warn("[LicenseService] License file tampered — forcing revalidation");
                        info.LastValidatedAt = null; // force online check
                    }
                }

                return info;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[LicenseService] Cannot read license file: {ex.Message}");
                return null;
            }
        }

        private void SaveLicenseFile(LicenseInfo info)
        {
            try
            {
                info.ValidationHash = ComputeHash(info);
                var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(LicensePath, json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[LicenseService] Cannot save license file: {ex.Message}");
            }
        }

        private void DeleteLicenseFile()
        {
            try { if (File.Exists(LicensePath)) File.Delete(LicensePath); } catch { }
        }

        private static string ComputeHash(LicenseInfo info)
        {
            var data = $"{info.LicenseKey}|{info.MachineId}|{info.Siid}|{PRODUCT_NAME}";
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash)[..24];
        }

        // ── Utilities ──────────────────────────────────────────────

        private static LicenseType ParseType(string? t) => t?.ToLowerInvariant() switch
        {
            "standard" => LicenseType.Standard,
            "professional" => LicenseType.Professional,
            "enterprise" => LicenseType.Enterprise,
            "lifetime" => LicenseType.Lifetime,
            _ => LicenseType.Standard
        };

        private static LicenseResult Fail(string msg) => new() { IsValid = false, Message = msg };
        private void Emit(string type, string msg) => OnLogMessage?.Invoke(type, msg);
    }

    // ══════════════════════════════════════════════════════════════
    //  MODELS
    // ══════════════════════════════════════════════════════════════

    public class LicenseInfo
    {
        public string LicenseKey { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string? CustomerEmail { get; set; }
        public LicenseType Type { get; set; } = LicenseType.None;
        public DateTime? ExpiresAt { get; set; }
        public string? MachineId { get; set; }
        public string? Siid { get; set; }
        public string? CombinedId { get; set; }
        public DateTime ActivatedAt { get; set; }
        public DateTime? LastValidatedAt { get; set; }
        public int MaxMachines { get; set; } = 1;
        public int MachinesUsed { get; set; } = 1;
        public string? ValidationHash { get; set; }

        [JsonIgnore]
        public bool IsValid => (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);
    }

    public class LicenseResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = "";
        public LicenseInfo? LicenseData { get; set; }
        public int GraceDaysRemaining { get; set; }
    }

    public enum LicenseType
    {
        None,
        Standard,
        Professional,
        Enterprise,
        Lifetime
    }

    public class LicenseApiResponse
    {
        public bool Valid { get; set; }
        public string? LicensedTo { get; set; }
        public string? Email { get; set; }
        public string? LicenseType { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public int? MaxMachines { get; set; }
        public int? MachinesUsed { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
    }
}
