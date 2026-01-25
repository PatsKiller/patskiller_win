using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Service to log all PatsKiller Pro activities to the backend
    /// Logs are stored in pro_activity_logs table for admin monitoring
    /// </summary>
    public class ProActivityLogger
    {
        private static ProActivityLogger? _instance;
        private static readonly object _lock = new object();
        
        public static ProActivityLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ProActivityLogger();
                    }
                }
                return _instance;
            }
        }

        private readonly HttpClient _httpClient;
        private const string SUPABASE_URL = "https://kmpnplpijuzzbftsjacx.supabase.co";
        private const string SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpanV6emJmdHNqYWN4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3MzA5ODgwMTgsImV4cCI6MjA0NjU2NDAxOH0.iqKMFa_Ye7LCG-n7F1a1rgdsVBPkz3TmT_x0lMm8TT8";

        // User context (set after login)
        public string? AuthToken { get; set; }
        public string? UserEmail { get; set; }
        public string? UserId { get; set; }
        public string MachineId { get; private set; }
        public string? AppVersion { get; set; }

        private ProActivityLogger()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            MachineId = GenerateMachineId();
            AppVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "2.0.0";
        }

        private static string GenerateMachineId()
        {
            try
            {
                var data = Environment.MachineName + Environment.UserName + Environment.OSVersion.ToString();
                using var sha = System.Security.Cryptography.SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(data));
                return Convert.ToBase64String(hash)[..16];
            }
            catch
            {
                return Environment.MachineName;
            }
        }

        /// <summary>
        /// Set auth context after login
        /// </summary>
        public void SetAuthContext(string authToken, string userEmail, string? userId = null)
        {
            AuthToken = authToken;
            UserEmail = userEmail;
            UserId = userId;
        }

        /// <summary>
        /// Clear auth context on logout
        /// </summary>
        public void ClearAuthContext()
        {
            AuthToken = null;
            UserEmail = null;
            UserId = null;
        }

        /// <summary>
        /// Log an activity to the backend (async)
        /// </summary>
        public async Task LogActivityAsync(ActivityLogEntry entry)
        {
            if (string.IsNullOrEmpty(AuthToken))
            {
                Logger.Debug("[ProActivityLogger] No auth token, skipping log");
                return;
            }

            try
            {
                var logData = new
                {
                    user_email = UserEmail ?? entry.UserEmail,
                    user_id = UserId,
                    action = entry.Action,
                    action_category = entry.ActionCategory,
                    details = entry.Details,
                    token_change = entry.TokenChange,
                    machine_id = MachineId,
                    vin = entry.Vin,
                    vehicle_year = entry.VehicleYear,
                    vehicle_model = entry.VehicleModel,
                    success = entry.Success,
                    error_message = entry.ErrorMessage,
                    response_time_ms = entry.ResponseTimeMs,
                    app_version = AppVersion,
                    metadata = entry.Metadata
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/log-pro-activity");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                request.Headers.Add("Authorization", $"Bearer {AuthToken}");
                request.Content = new StringContent(JsonSerializer.Serialize(logData), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Logger.Warning($"[ProActivityLogger] Failed to log activity: {response.StatusCode} - {error}");
                }
                else
                {
                    Logger.Debug($"[ProActivityLogger] Logged: {entry.Action}");
                }
            }
            catch (Exception ex)
            {
                // Don't fail the main operation if logging fails
                Logger.Warning($"[ProActivityLogger] Exception logging activity: {ex.Message}");
            }
        }

        /// <summary>
        /// Fire and forget logging (doesn't block main operation)
        /// </summary>
        public void LogActivity(ActivityLogEntry entry)
        {
            _ = Task.Run(() => LogActivityAsync(entry));
        }

        // ============ CONVENIENCE METHODS ============

        public void LogLogin(string email, bool success, string? errorMessage = null, int responseTimeMs = 0)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "login",
                ActionCategory = "auth",
                UserEmail = email,
                Success = success,
                ErrorMessage = errorMessage,
                ResponseTimeMs = responseTimeMs,
                Details = success ? "User logged in via Google OAuth" : $"Login failed: {errorMessage}"
            });
        }

        public void LogLogout(string email)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "logout",
                ActionCategory = "auth",
                UserEmail = email,
                Success = true,
                Details = "User logged out"
            });
        }

        public void LogIncodeCalculation(string vin, string? vehicleYear, string? vehicleModel, 
            bool success, int tokenChange, int responseTimeMs, string? errorMessage = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "incode_calculation",
                ActionCategory = "operation",
                Vin = vin,
                VehicleYear = vehicleYear,
                VehicleModel = vehicleModel,
                Success = success,
                TokenChange = tokenChange,
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = errorMessage,
                Details = success ? $"Calculated incode for {vin}" : $"Incode calculation failed: {errorMessage}"
            });
        }

        public void LogOutcodeCalculation(string outcode, bool success, int tokenChange, 
            int responseTimeMs, string? errorMessage = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "outcode_calculation",
                ActionCategory = "operation",
                Success = success,
                TokenChange = tokenChange,
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = errorMessage,
                Details = success ? "Calculated outcode" : $"Outcode calculation failed: {errorMessage}"
            });
        }

        public void LogKeyProgramming(string vin, string? vehicleYear, string? vehicleModel,
            bool success, int tokenChange, int responseTimeMs, string? errorMessage = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "key_programming",
                ActionCategory = "key_programming",
                Vin = vin,
                VehicleYear = vehicleYear,
                VehicleModel = vehicleModel,
                Success = success,
                TokenChange = tokenChange,
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = errorMessage,
                Details = success ? $"Programmed key for {vin}" : $"Key programming failed: {errorMessage}"
            });
        }

        public void LogEraseAllKeys(string vin, string? vehicleYear, string? vehicleModel,
            bool success, int tokenChange, int responseTimeMs, string? errorMessage = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "erase_all_keys",
                ActionCategory = "key_programming",
                Vin = vin,
                VehicleYear = vehicleYear,
                VehicleModel = vehicleModel,
                Success = success,
                TokenChange = tokenChange,
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = errorMessage,
                Details = success ? $"Erased all keys for {vin}" : $"Erase keys failed: {errorMessage}"
            });
        }

        public void LogParameterReset(string vin, string? vehicleYear, string? vehicleModel,
            bool success, int tokenChange, int responseTimeMs, string? errorMessage = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "parameter_reset",
                ActionCategory = "operation",
                Vin = vin,
                VehicleYear = vehicleYear,
                VehicleModel = vehicleModel,
                Success = success,
                TokenChange = tokenChange,
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = errorMessage,
                Details = success ? $"Reset parameters for {vin}" : $"Parameter reset failed: {errorMessage}"
            });
        }

        public void LogInitializeESCL(string vin, string? vehicleYear, string? vehicleModel,
            bool success, int tokenChange, int responseTimeMs, string? errorMessage = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "initialize_escl",
                ActionCategory = "operation",
                Vin = vin,
                VehicleYear = vehicleYear,
                VehicleModel = vehicleModel,
                Success = success,
                TokenChange = tokenChange,
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = errorMessage,
                Details = success ? $"Initialized ESCL for {vin}" : $"ESCL init failed: {errorMessage}"
            });
        }

        public void LogDisableBCM(string vin, string? vehicleYear, string? vehicleModel,
            bool success, int tokenChange, int responseTimeMs, string? errorMessage = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "disable_bcm_security",
                ActionCategory = "operation",
                Vin = vin,
                VehicleYear = vehicleYear,
                VehicleModel = vehicleModel,
                Success = success,
                TokenChange = tokenChange,
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = errorMessage,
                Details = success ? $"Disabled BCM security for {vin}" : $"BCM disable failed: {errorMessage}"
            });
        }

        public void LogVehicleDetection(string vin, string? vehicleYear, string? vehicleModel,
            bool success, int responseTimeMs, string? errorMessage = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "vehicle_detection",
                ActionCategory = "diagnostic",
                Vin = vin,
                VehicleYear = vehicleYear,
                VehicleModel = vehicleModel,
                Success = success,
                TokenChange = 0, // No token cost for detection
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = errorMessage,
                Details = success ? $"Detected vehicle: {vehicleYear} {vehicleModel}" : $"Detection failed: {errorMessage}"
            });
        }

        public void LogJ2534Connection(string deviceName, bool success, string? errorMessage = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "j2534_connect",
                ActionCategory = "diagnostic",
                Success = success,
                ErrorMessage = errorMessage,
                Details = success ? $"Connected to J2534 device: {deviceName}" : $"J2534 connection failed: {errorMessage}",
                Metadata = new { device_name = deviceName }
            });
        }

        public void LogJ2534Disconnect(string deviceName)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "j2534_disconnect",
                ActionCategory = "diagnostic",
                Success = true,
                Details = $"Disconnected from J2534 device: {deviceName}"
            });
        }

        public void LogAppStart()
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "app_start",
                ActionCategory = "app",
                Success = true,
                Details = $"PatsKiller Pro started - Version {AppVersion}",
                Metadata = new 
                { 
                    app_version = AppVersion,
                    os_version = Environment.OSVersion.ToString(),
                    machine_name = Environment.MachineName
                }
            });
        }

        public void LogAppClose()
        {
            // Use sync call for app close to ensure it completes
            try
            {
                LogActivityAsync(new ActivityLogEntry
                {
                    Action = "app_close",
                    ActionCategory = "app",
                    Success = true,
                    Details = "PatsKiller Pro closed"
                }).Wait(TimeSpan.FromSeconds(2));
            }
            catch { /* Ignore errors on close */ }
        }

        public void LogError(string action, string errorMessage, string? details = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = action,
                ActionCategory = "error",
                Success = false,
                ErrorMessage = errorMessage,
                Details = details ?? errorMessage
            });
        }
    }

    /// <summary>
    /// Activity log entry structure
    /// </summary>
    public class ActivityLogEntry
    {
        public string Action { get; set; } = "";
        public string ActionCategory { get; set; } = "operation"; // auth, operation, key_programming, diagnostic, app, error
        public string? UserEmail { get; set; }
        public string? Vin { get; set; }
        public string? VehicleYear { get; set; }
        public string? VehicleModel { get; set; }
        public bool Success { get; set; } = true;
        public int TokenChange { get; set; } = 0;
        public int ResponseTimeMs { get; set; } = 0;
        public string? ErrorMessage { get; set; }
        public string? Details { get; set; }
        public object? Metadata { get; set; }
    }
}
