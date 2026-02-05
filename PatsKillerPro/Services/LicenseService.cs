using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Manages PatsKiller Pro license validation, activation, and periodic heartbeat.
    ///
    /// Design choices (per Licensing Integration Design Spec v1):
    ///   • Hybrid Auth – license OR SSO grants app access; tokens always need SSO.
    ///   • Machine binding – MachineIdentity.CombinedId (hardware + SIID).
    ///   • Offline strategy – validate online weekly; 3-day grace; 10-day hard lock.
    ///   • Cache – AES-encrypted JSON in %LocalAppData%\PatsKillerPro\license.dat
    ///            with HMAC tamper detection.
    ///   • Heartbeat – every 4 h while running (background timer).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class LicenseService : IDisposable
    {
        // ───────────────────────── Singleton ─────────────────────────
        private static LicenseService? _instance;
        private static readonly object _lock = new();
        public static LicenseService Instance
        {
            get
            {
                if (_instance == null)
                    lock (_lock) { _instance ??= new LicenseService(); }
                return _instance;
            }
        }

        // ───────────────────────── Constants ─────────────────────────
        private const string LICENSE_API = "https://kmpnplpijuzzbftsjacx.supabase.co/functions/v1/bridge-license";
        private const string SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpanV6emJmdHNqYWN4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3NjQyNjc0MDUsImV4cCI6MjA3OTg0MzQwNX0.RLX0e1FAq7AlKIpVXhaw7J3ILY_yc0FJDoAzxQDy24E";
        private const string APP_FOLDER = "PatsKillerPro";
        // Spec calls this out as "license.key" (encrypted JSON). Keep backward compatibility
        // for early builds that used "license.dat".
        private const string LICENSE_FILE = "license.key";  // encrypted cache
        private const string LEGACY_LICENSE_FILE = "license.dat";
        private const int REVALIDATION_DAYS = 7;   // online check every 7 days
        private const int GRACE_PERIOD_DAYS = 3;    // 3 extra days if offline
        private const int HEARTBEAT_HOURS = 4;      // background heartbeat interval

        // AES encryption key derived from a stable machine secret.
        // This prevents copying the license.dat to another machine.
        private static readonly byte[] _encSalt = Encoding.UTF8.GetBytes("PatsKillerPro_LicCache_2026");

        // ───────────────────────── State ─────────────────────────────
        private readonly HttpClient _http;
        private System.Threading.Timer? _heartbeatTimer;
        private LicenseCacheData? _cache;
        private bool _disposed;

        // ───────────────────────── Public Properties ─────────────────
        public bool IsLicensed => _cache?.IsCurrentlyValid(MachineIdentity.MachineId, MachineIdentity.SIID) == true;
        public string? LicensedTo => _cache?.CustomerName;
        public string? CustomerEmail => _cache?.CustomerEmail;
        public string? LicenseType => _cache?.LicenseType;
        public DateTime? ExpiresAt => _cache?.ExpiresAt;
        public DateTime? ActivatedAt => _cache?.ActivatedAt;
        public DateTime? LastValidatedAt => _cache?.LastValidatedAt;
        public int MaxMachines => _cache?.MaxMachines ?? 0;
        public int MachinesUsed => _cache?.MachinesUsed ?? 0;
        public string? LicenseKey => _cache?.LicenseKey;

        /// <summary>True if the cached license needs online revalidation (>7 days).</summary>
        public bool NeedsRevalidation => _cache != null &&
            (DateTime.UtcNow - _cache.LastValidatedAt).TotalDays > REVALIDATION_DAYS;

        /// <summary>True if in grace period (7-10 days offline).</summary>
        public bool InGracePeriod
        {
            get
            {
                if (_cache == null) return false;
                var days = (DateTime.UtcNow - _cache.LastValidatedAt).TotalDays;
                return days > REVALIDATION_DAYS && days <= (REVALIDATION_DAYS + GRACE_PERIOD_DAYS);
            }
        }

        /// <summary>Days remaining in grace period (0 if not in grace).</summary>
        public int GraceDaysRemaining
        {
            get
            {
                if (_cache == null) return 0;
                var days = (DateTime.UtcNow - _cache.LastValidatedAt).TotalDays;
                var remaining = (REVALIDATION_DAYS + GRACE_PERIOD_DAYS) - (int)days;
                return Math.Max(0, remaining);
            }
        }

        // ───────────────────────── Events ────────────────────────────
        /// <summary>
        /// (type, message) for UI log panel.  type: "info" | "success" | "warning" | "error"
        /// </summary>
        public event Action<string, string>? OnLogMessage;

        /// <summary>Fires when license state changes (valid→invalid, new activation, etc.).</summary>
        public event Action<LicenseValidationResult>? OnLicenseChanged;

        // ───────────────────────── Constructor ───────────────────────
        private LicenseService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.Add("apikey", SUPABASE_ANON_KEY);
        }

        // ═══════════════════════════════════════════════════════════════
        //  VALIDATE  (called on startup, checks cache → online if needed)
        // ═══════════════════════════════════════════════════════════════
        /// <summary>
        /// Full validation: loads cache, verifies machine binding + expiry,
        /// revalidates online if >7 days since last check.
        /// </summary>
        public async Task<LicenseValidationResult> ValidateAsync()
        {
            LogUI("info", "[License] Validating...");

            // 1. Load cached license
            _cache = LoadCache();
            if (_cache == null)
            {
                LogUI("info", "[License] No cached license found");
                return Emit(LicenseValidationResult.NoLicense());
            }

            // 2. Machine binding check
            if (_cache.MachineId != MachineIdentity.MachineId)
            {
                LogUI("error", "[License] Machine ID mismatch – hardware changed?");
                return Emit(LicenseValidationResult.Fail("License is bound to a different machine (hardware changed)"));
            }
            if (_cache.SIID != MachineIdentity.SIID)
            {
                LogUI("error", "[License] SIID mismatch – re-installed?");
                return Emit(LicenseValidationResult.Fail("License is bound to a different installation (SIID changed)"));
            }

            // 3. Tamper check
            if (!VerifyHash(_cache))
            {
                LogUI("error", "[License] Cache tamper detected");
                ClearCache();
                return Emit(LicenseValidationResult.Fail("License cache integrity check failed"));
            }

            // 4. Expiration check
            if (_cache.ExpiresAt.HasValue && _cache.ExpiresAt.Value < DateTime.UtcNow)
            {
                LogUI("warning", $"[License] Expired on {_cache.ExpiresAt:d}");
                return Emit(LicenseValidationResult.Expired(_cache.ExpiresAt.Value));
            }

            // 5. Revalidation check (weekly)
            var daysSince = (DateTime.UtcNow - _cache.LastValidatedAt).TotalDays;
            if (daysSince > REVALIDATION_DAYS)
            {
                LogUI("info", $"[License] Last validated {(int)daysSince}d ago, checking online...");
                var online = await RevalidateOnlineAsync();
                if (online.IsValid)
                {
                    StartHeartbeat();
                    return online;
                }

                // Offline: check grace period
                if (daysSince <= (REVALIDATION_DAYS + GRACE_PERIOD_DAYS))
                {
                    var grace = (int)(REVALIDATION_DAYS + GRACE_PERIOD_DAYS - daysSince);
                    LogUI("warning", $"[License] Offline – {grace} grace days remaining");
                    StartHeartbeat();
                    return Emit(LicenseValidationResult.GracePeriod(_cache, grace));
                }

                // Hard lock
                LogUI("error", "[License] Offline too long – must connect to internet");
                return Emit(LicenseValidationResult.MustConnect());
            }

            // 6. Valid cached license
            LogUI("success", $"[License] Valid – licensed to {_cache.CustomerName}");
            StartHeartbeat();
            return Emit(LicenseValidationResult.Valid(_cache));
        }

        // ═══════════════════════════════════════════════════════════════
        //  ACTIVATE  (user enters a license key)
        // ═══════════════════════════════════════════════════════════════
        public async Task<LicenseValidationResult> ActivateAsync(string licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
                return Emit(LicenseValidationResult.Fail("License key cannot be empty"));

            licenseKey = licenseKey.Trim().ToUpperInvariant();
            LogUI("info", $"[License] Activating key: {licenseKey[..Math.Min(4, licenseKey.Length)]}...");

            try
            {
                var request = new
                {
                    action = "validate",
                    license_key = licenseKey,
                    machine_id = MachineIdentity.CombinedId,
                    siid = MachineIdentity.SIID,
                    machine_name = Environment.MachineName,
                    version = GetAppVersion()
                };

                var response = await _http.PostAsync(LICENSE_API,
                    new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));

                var json = await response.Content.ReadAsStringAsync();
                Logger.Debug($"[LicenseService] Activate response: HTTP {(int)response.StatusCode} – {json}");

                var api = JsonSerializer.Deserialize<LicenseApiResponse>(json, _jsonOpts);
                if (api == null)
                    return Emit(LicenseValidationResult.Fail("Invalid server response"));

                if (api.Valid)
                {
                    // Build + save cache
                    _cache = new LicenseCacheData
                    {
                        LicenseKey = licenseKey,
                        CustomerName = api.LicensedTo ?? "Unknown",
                        CustomerEmail = api.Email,
                        LicenseType = api.LicenseType ?? "standard",
                        ExpiresAt = api.ExpiresAt,
                        MaxMachines = api.MaxMachines ?? 1,
                        MachinesUsed = api.MachinesUsed ?? 1,
                        MachineId = MachineIdentity.MachineId,
                        SIID = MachineIdentity.SIID,
                        ActivatedAt = DateTime.UtcNow,
                        LastValidatedAt = DateTime.UtcNow
                    };
                    _cache.ValidationHash = ComputeHash(_cache);
                    SaveCache(_cache);

                    LogUI("success", $"[License] ✓ Activated for {_cache.CustomerName} ({_cache.LicenseType})");
                    StartHeartbeat();
                    return Emit(LicenseValidationResult.Valid(_cache));
                }

                // Handle specific errors
                var msg = api.Error switch
                {
                    "invalid_key" => "License key not found",
                    "machine_limit" => $"Machine limit reached ({api.MachinesUsed}/{api.MaxMachines}). Deactivate another machine first.",
                    "expired" => $"License expired on {api.ExpiresAt:d}",
                    "disabled" => api.Message ?? "License has been disabled",
                    _ => api.Message ?? "Activation failed"
                };

                LogUI("error", $"[License] ✗ {msg}");
                return Emit(LicenseValidationResult.Fail(msg));
            }
            catch (HttpRequestException ex)
            {
                LogUI("error", $"[License] ✗ Cannot reach server: {ex.Message}");
                return Emit(LicenseValidationResult.Fail("Cannot reach license server – check internet connection"));
            }
            catch (TaskCanceledException)
            {
                LogUI("error", "[License] ✗ Server timeout");
                return Emit(LicenseValidationResult.Fail("License server timeout – try again"));
            }
            catch (Exception ex)
            {
                LogUI("error", $"[License] ✗ Unexpected error: {ex.Message}");
                return Emit(LicenseValidationResult.Fail($"Activation error: {ex.Message}"));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  DEACTIVATE  (release machine slot)
        // ═══════════════════════════════════════════════════════════════
        public async Task<bool> DeactivateAsync()
        {
            if (_cache == null || string.IsNullOrEmpty(_cache.LicenseKey))
            {
                LogUI("warning", "[License] No active license to deactivate");
                return false;
            }

            LogUI("info", "[License] Deactivating...");

            try
            {
                var request = new
                {
                    action = "deactivate",
                    license_key = _cache.LicenseKey,
                    machine_id = MachineIdentity.CombinedId
                };

                var response = await _http.PostAsync(LICENSE_API,
                    new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));

                var json = await response.Content.ReadAsStringAsync();
                Logger.Debug($"[LicenseService] Deactivate response: HTTP {(int)response.StatusCode} – {json}");

                ClearCache();
                StopHeartbeat();

                LogUI("success", "[License] ✓ License deactivated from this machine");
                Emit(LicenseValidationResult.NoLicense());
                return true;
            }
            catch (Exception ex)
            {
                LogUI("error", $"[License] ✗ Deactivation error: {ex.Message}");
                // Still clear local cache – user can re-activate
                ClearCache();
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HEARTBEAT  (periodic check-in)
        // ═══════════════════════════════════════════════════════════════
        public async Task<bool> HeartbeatAsync()
        {
            if (_cache == null || string.IsNullOrEmpty(_cache.LicenseKey))
                return false;

            try
            {
                var request = new
                {
                    action = "heartbeat",
                    license_key = _cache.LicenseKey,
                    machine_id = MachineIdentity.CombinedId,
                    version = GetAppVersion()
                };

                var response = await _http.PostAsync(LICENSE_API,
                    new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));

                var json = await response.Content.ReadAsStringAsync();
                var api = JsonSerializer.Deserialize<LicenseApiResponse>(json, _jsonOpts);

                if (api?.Valid == true)
                {
                    // Update last validated timestamp
                    _cache.LastValidatedAt = DateTime.UtcNow;
                    _cache.ValidationHash = ComputeHash(_cache);
                    SaveCache(_cache);
                    Logger.Debug("[LicenseService] Heartbeat OK");
                    return true;
                }

                if (api?.Error == "expired" || api?.Error == "disabled")
                {
                    LogUI("error", $"[License] Server says: {api.Message ?? api.Error}");
                    ClearCache();
                    Emit(LicenseValidationResult.Fail(api.Message ?? "License no longer valid"));
                    return false;
                }

                Logger.Warning($"[LicenseService] Heartbeat returned: {json}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Debug($"[LicenseService] Heartbeat failed (non-critical): {ex.Message}");
                return false; // not critical – cache still valid
            }
        }

        // ───────────────────────── Online Revalidation ───────────────
        private async Task<LicenseValidationResult> RevalidateOnlineAsync()
        {
            if (_cache == null) return LicenseValidationResult.NoLicense();

            try
            {
                var request = new
                {
                    action = "validate",
                    license_key = _cache.LicenseKey,
                    machine_id = MachineIdentity.CombinedId,
                    siid = MachineIdentity.SIID,
                    machine_name = Environment.MachineName,
                    version = GetAppVersion()
                };

                var response = await _http.PostAsync(LICENSE_API,
                    new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));

                var json = await response.Content.ReadAsStringAsync();
                var api = JsonSerializer.Deserialize<LicenseApiResponse>(json, _jsonOpts);

                if (api?.Valid == true)
                {
                    // Refresh cache with latest server data
                    _cache.CustomerName = api.LicensedTo ?? _cache.CustomerName;
                    _cache.CustomerEmail = api.Email ?? _cache.CustomerEmail;
                    _cache.LicenseType = api.LicenseType ?? _cache.LicenseType;
                    _cache.ExpiresAt = api.ExpiresAt ?? _cache.ExpiresAt;
                    _cache.MaxMachines = api.MaxMachines ?? _cache.MaxMachines;
                    _cache.MachinesUsed = api.MachinesUsed ?? _cache.MachinesUsed;
                    _cache.LastValidatedAt = DateTime.UtcNow;
                    _cache.ValidationHash = ComputeHash(_cache);
                    SaveCache(_cache);

                    LogUI("success", $"[License] ✓ Revalidated online – {_cache.CustomerName}");
                    return Emit(LicenseValidationResult.Valid(_cache));
                }

                // Server rejected – license revoked/expired
                if (api != null)
                {
                    LogUI("error", $"[License] Server rejected: {api.Message ?? api.Error}");
                    return Emit(LicenseValidationResult.Fail(api.Message ?? "License no longer valid"));
                }

                // Could not parse response – treat as offline
                return LicenseValidationResult.Fail("Invalid server response");
            }
            catch (Exception ex)
            {
                Logger.Debug($"[LicenseService] Revalidation failed (offline?): {ex.Message}");
                // Return fail so caller can check grace period
                return LicenseValidationResult.Fail($"Offline: {ex.Message}");
            }
        }

        // ───────────────────────── Heartbeat Timer ───────────────────
        private void StartHeartbeat()
        {
            if (_heartbeatTimer != null) return;
            _heartbeatTimer = new System.Threading.Timer(async _ =>
            {
                try { await HeartbeatAsync(); }
                catch (Exception ex) { Logger.Debug($"[LicenseService] Heartbeat timer error: {ex.Message}"); }
            }, null, TimeSpan.FromHours(HEARTBEAT_HOURS), TimeSpan.FromHours(HEARTBEAT_HOURS));
        }

        private void StopHeartbeat()
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  CACHE (encrypted local storage)
        // ═══════════════════════════════════════════════════════════════
        private static string CacheFolder
        {
            get
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    APP_FOLDER);
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        private static string CachePath => Path.Combine(CacheFolder, LICENSE_FILE);
        private static string LegacyCachePath => Path.Combine(CacheFolder, LEGACY_LICENSE_FILE);

        private LicenseCacheData? LoadCache()
        {
            try
            {
                // Prefer spec file name (license.key), but fall back to older builds (license.dat)
                var path = File.Exists(CachePath) ? CachePath : (File.Exists(LegacyCachePath) ? LegacyCachePath : null);
                if (path == null) return null;

                var encrypted = File.ReadAllBytes(path);
                var json = DecryptData(encrypted);
                if (string.IsNullOrEmpty(json)) return null;

                return JsonSerializer.Deserialize<LicenseCacheData>(json, _jsonOpts);
            }
            catch (Exception ex)
            {
                Logger.Warning($"[LicenseService] Cache load failed: {ex.Message}");
                return null;
            }
        }

        private void SaveCache(LicenseCacheData data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, _jsonOpts);
                var encrypted = EncryptData(json);
                File.WriteAllBytes(CachePath, encrypted);
                Logger.Debug("[LicenseService] Cache saved");
            }
            catch (Exception ex)
            {
                Logger.Error($"[LicenseService] Cache save failed: {ex.Message}");
            }
        }

        private void ClearCache()
        {
            _cache = null;
            try { if (File.Exists(CachePath)) File.Delete(CachePath); } catch { /* best effort */ }
            try { if (File.Exists(LegacyCachePath)) File.Delete(LegacyCachePath); } catch { /* best effort */ }
        }

        // ───────────── Encryption (DPAPI + AES, machine-bound) ──────
        private static byte[] EncryptData(string plaintext)
        {
            var plain = Encoding.UTF8.GetBytes(plaintext);
            return ProtectedData.Protect(plain, _encSalt, DataProtectionScope.CurrentUser);
        }

        private static string? DecryptData(byte[] encrypted)
        {
            try
            {
                var plain = ProtectedData.Unprotect(encrypted, _encSalt, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return null; // wrong machine / user / corrupted
            }
        }

        // ───────────── Tamper detection (HMAC) ──────────────────────
        private static string ComputeHash(LicenseCacheData data)
        {
            var input = $"{data.LicenseKey}|{data.MachineId}|{data.SIID}|{data.ExpiresAt:O}|{data.LicenseType}|PatsKillerPro2026";
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hash);
        }

        private static bool VerifyHash(LicenseCacheData data)
        {
            if (string.IsNullOrEmpty(data.ValidationHash)) return false;
            return data.ValidationHash == ComputeHash(data);
        }

        // ───────────────────────── Helpers ───────────────────────────
        private void LogUI(string type, string message)
        {
            Logger.Log(type == "error" ? Logger.LogLevel.Error :
                       type == "warning" ? Logger.LogLevel.Warning : Logger.LogLevel.Info, message);
            try { OnLogMessage?.Invoke(type, message); } catch { /* ignore UI callback errors */ }
        }

        private LicenseValidationResult Emit(LicenseValidationResult result)
        {
            try { OnLicenseChanged?.Invoke(result); } catch { /* ignore */ }
            return result;
        }

        private static string GetAppVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "2.0.0";
        }

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopHeartbeat();
            _http.Dispose();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MODELS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Encrypted cache stored in %LocalAppData%\PatsKillerPro\license.dat</summary>
    public class LicenseCacheData
    {
        public string LicenseKey { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string? CustomerEmail { get; set; }
        public string LicenseType { get; set; } = "standard";
        public DateTime? ExpiresAt { get; set; }
        public int MaxMachines { get; set; } = 1;
        public int MachinesUsed { get; set; } = 1;

        // Machine binding
        public string MachineId { get; set; } = "";
        public string SIID { get; set; } = "";

        // Timestamps
        public DateTime ActivatedAt { get; set; }
        public DateTime LastValidatedAt { get; set; }

        // Tamper detection
        public string? ValidationHash { get; set; }

        /// <summary>True if license is within expiry and bound to the given machine.</summary>
        public bool IsCurrentlyValid(string machineId, string siid)
        {
            if (MachineId != machineId || SIID != siid) return false;
            if (ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow) return false;
            // Allow up to 10 days without revalidation (7 + 3 grace)
            if ((DateTime.UtcNow - LastValidatedAt).TotalDays > 10) return false;
            return true;
        }
    }

    /// <summary>Result of a license validation or activation attempt.</summary>
    public class LicenseValidationResult
    {
        public bool IsValid { get; init; }
        public string Message { get; init; } = "";
        public string? LicensedTo { get; init; }
        public string? LicenseType { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public int MaxMachines { get; init; }
        public int MachinesUsed { get; init; }
        public int GraceDaysRemaining { get; init; }
        public bool IsGracePeriod { get; init; }
        public bool RequiresReconnect { get; init; }
        public bool IsExpired { get; init; }
        public bool HasLicense { get; init; } = true;

        public static LicenseValidationResult Valid(LicenseCacheData cache) => new()
        {
            IsValid = true,
            Message = $"Licensed to {cache.CustomerName}",
            LicensedTo = cache.CustomerName,
            LicenseType = cache.LicenseType,
            ExpiresAt = cache.ExpiresAt,
            MaxMachines = cache.MaxMachines,
            MachinesUsed = cache.MachinesUsed
        };

        public static LicenseValidationResult GracePeriod(LicenseCacheData cache, int daysRemaining) => new()
        {
            IsValid = true, // still usable
            IsGracePeriod = true,
            GraceDaysRemaining = daysRemaining,
            Message = $"License valid (offline) – {daysRemaining} days until re-check required",
            LicensedTo = cache.CustomerName,
            LicenseType = cache.LicenseType,
            ExpiresAt = cache.ExpiresAt,
            MaxMachines = cache.MaxMachines,
            MachinesUsed = cache.MachinesUsed
        };

        public static LicenseValidationResult Expired(DateTime expiredAt) => new()
        {
            IsValid = false, IsExpired = true,
            Message = $"License expired on {expiredAt:d}",
            ExpiresAt = expiredAt
        };

        public static LicenseValidationResult MustConnect() => new()
        {
            IsValid = false, RequiresReconnect = true,
            Message = "Offline too long – must connect to internet to revalidate"
        };

        public static LicenseValidationResult NoLicense() => new()
        {
            IsValid = false, HasLicense = false,
            Message = "No license key found"
        };

        public static LicenseValidationResult Fail(string message) => new()
        {
            IsValid = false, Message = message
        };
    }

    /// <summary>Response from bridge-license edge function.</summary>
    internal class LicenseApiResponse
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
        public string? ServerTime { get; set; }
        public string? NextCheckBy { get; set; }
    }
}
