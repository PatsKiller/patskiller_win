using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Logs all PatsKiller Pro activities to pro_activity_logs table.
    /// 
    /// TOKEN COSTS:
    /// - Key Session: 1 token (unlimited keys while same outcode)
    /// - Parameter Reset: 1 token per module (PCM, BCM, IPC, TCM = 3-4 total)
    /// - Utility Operations: 1 token each
    /// - Gateway Unlock: 1 token
    /// - Diagnostics/Detection: FREE
    /// </summary>
    public class ProActivityLogger
    {
        private static ProActivityLogger? _instance;
        private static readonly object _lock = new object();
        
        /// <summary>
        /// Event for UI logging - subscribe to show messages in the log panel
        /// </summary>
        public event Action<string, string>? OnLogMessage; // (type, message) - type: "info", "success", "warning", "error"
        
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

        public string? AuthToken { get; set; }
        public string? UserEmail { get; set; }
        public string? UserId { get; set; }
        public string MachineId { get; private set; }
        public string? AppVersion { get; set; }
        
        private void LogToUI(string type, string message)
        {
            Logger.Log(type == "error" ? LogLevel.Error : type == "warning" ? LogLevel.Warning : LogLevel.Info, message);
            try { OnLogMessage?.Invoke(type, message); } catch { /* ignore UI callback errors */ }
        }

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

        public void SetAuthContext(string authToken, string userEmail, string? userId = null)
        {
            AuthToken = authToken;
            UserEmail = userEmail;
            UserId = userId;
            
            if (!string.IsNullOrEmpty(authToken))
            {
                LogToUI("info", $"[Activity] Auth set for {userEmail}");
            }
            else
            {
                LogToUI("warning", "[Activity] SetAuthContext called with empty token!");
            }
        }

        public void ClearAuthContext()
        {
            LogToUI("info", $"[Activity] Auth cleared (was: {UserEmail})");
            AuthToken = null;
            UserEmail = null;
            UserId = null;
        }

        public async Task LogActivityAsync(ActivityLogEntry entry)
        {
            if (string.IsNullOrEmpty(AuthToken))
            {
                LogToUI("warning", $"[Activity] Skipped '{entry.Action}' - not logged in");
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

                var url = $"{SUPABASE_URL}/functions/v1/log-pro-activity";
                
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                request.Headers.Add("Authorization", $"Bearer {AuthToken}");
                request.Content = new StringContent(JsonSerializer.Serialize(logData), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    LogToUI("success", $"[Activity] ✓ {entry.Action}");
                }
                else
                {
                    LogToUI("error", $"[Activity] ✗ {entry.Action} failed: HTTP {(int)response.StatusCode}");
                    Logger.Error($"[ProActivityLogger] Response: {responseBody}");
                }
            }
            catch (HttpRequestException ex)
            {
                LogToUI("error", $"[Activity] Network error: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                LogToUI("warning", $"[Activity] Timeout on '{entry.Action}'");
            }
            catch (Exception ex)
            {
                LogToUI("error", $"[Activity] Error: {ex.Message}");
            }
        }

        public void LogActivity(ActivityLogEntry entry)
        {
            _ = Task.Run(async () => 
            {
                try
                {
                    await LogActivityAsync(entry);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[ProActivityLogger] Background task error: {ex.Message}");
                }
            });
        }

        // ============ AUTH ============

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
                TokenChange = 0,
                Details = success ? "User logged in" : $"Login failed: {errorMessage}"
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
                TokenChange = 0,
                Details = "User logged out"
            });
        }

        // ============ KEY PROGRAMMING (1 token per session) ============

        public void LogKeySessionStart(string vin, string? year, string? model, string outcode,
            bool success, int tokenChange, int responseTimeMs, string? error = null)
        {
            // SECURITY: outcode passed for server-side validation but NOT included in telemetry metadata
            LogActivity(new ActivityLogEntry
            {
                Action = "key_session_start",
                ActionCategory = "key_programming",
                Vin = vin,
                VehicleYear = year,
                VehicleModel = model,
                Success = success,
                TokenChange = tokenChange,
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = error,
                Details = success ? $"Key session started for {vin}" : $"Session failed: {error}",
                Metadata = new { } // SECURITY: No outcode in telemetry
            });
        }

        public void LogKeyProgrammed(string vin, string? year, string? model, int keyNumber,
            bool success, int responseTimeMs, string? error = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "key_programmed",
                ActionCategory = "key_programming",
                Vin = vin,
                VehicleYear = year,
                VehicleModel = model,
                Success = success,
                TokenChange = 0, // FREE within session
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = error,
                Details = success ? $"Key #{keyNumber} programmed" : $"Key #{keyNumber} failed: {error}",
                Metadata = new { key_number = keyNumber }
            });
        }

        public void LogEraseAllKeys(string vin, string? year, string? model, int keysErased,
            bool success, int tokenChange, int responseTimeMs, string? error = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "erase_all_keys",
                ActionCategory = "key_programming",
                Vin = vin,
                VehicleYear = year,
                VehicleModel = model,
                Success = success,
                TokenChange = tokenChange, // 0 if in session
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = error,
                Details = success ? $"Erased {keysErased} keys" : $"Erase failed: {error}",
                Metadata = new { keys_erased = keysErased }
            });
        }

        // ============ PARAMETER RESET (1 token per module) ============

        public void LogParameterResetModule(string vin, string? year, string? model, string moduleName,
            string outcode, string incode, bool success, int tokenChange, int responseTimeMs, string? error = null)
        {
            // SECURITY: Never include incode in telemetry - only mask for reference
            LogActivity(new ActivityLogEntry
            {
                Action = $"param_reset_{moduleName.ToLowerInvariant()}",
                ActionCategory = "parameter_reset",
                Vin = vin,
                VehicleYear = year,
                VehicleModel = model,
                Success = success,
                TokenChange = tokenChange, // 1 token per module
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = error,
                Details = success ? $"{moduleName} reset complete" : $"{moduleName} reset failed: {error}",
                Metadata = new { module = moduleName } // SECURITY: No outcode/incode in telemetry
            });
        }

        public void LogParameterResetComplete(string vin, string? year, string? model,
            int modulesReset, int totalTokens, int responseTimeMs, string[]? modules = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "param_reset_complete",
                ActionCategory = "parameter_reset",
                Vin = vin,
                VehicleYear = year,
                VehicleModel = model,
                Success = true,
                TokenChange = 0, // Already charged per module
                ResponseTimeMs = responseTimeMs,
                Details = $"Parameter reset complete: {modulesReset} modules, {totalTokens} tokens",
                Metadata = new { modules_reset = modulesReset, total_tokens = totalTokens, modules }
            });
        }

        // ============ UTILITY (1 token each) ============

        public void LogUtilityOperation(string operation, string vin, string? year, string? model,
            bool success, int tokenChange, int responseTimeMs, string? error = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = $"utility_{operation.ToLowerInvariant().Replace(" ", "_")}",
                ActionCategory = "utility",
                Vin = vin,
                VehicleYear = year,
                VehicleModel = model,
                Success = success,
                TokenChange = tokenChange,
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = error,
                Details = success ? $"{operation} complete" : $"{operation} failed: {error}"
            });
        }

        // ============ DIAGNOSTICS (FREE) ============

        public void LogVehicleDetection(string vin, string? year, string? model,
            bool success, int responseTimeMs, string? error = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "vehicle_detection",
                ActionCategory = "diagnostic",
                Vin = vin,
                VehicleYear = year,
                VehicleModel = model,
                Success = success,
                TokenChange = 0,
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = error,
                Details = success ? $"Detected: {year} {model}" : $"Detection failed: {error}"
            });
        }

        public void LogJ2534Connection(string deviceName, bool success, string? error = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "j2534_connect",
                ActionCategory = "diagnostic",
                Success = success,
                TokenChange = 0,
                ErrorMessage = error,
                Details = success ? $"Connected to {deviceName}" : $"Connection failed: {error}",
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
                TokenChange = 0,
                Details = $"Disconnected from {deviceName}"
            });
        }

        // ============ APP LIFECYCLE ============

        public void LogAppStart()
        {
            LogActivity(new ActivityLogEntry
            {
                Action = "app_start",
                ActionCategory = "app",
                Success = true,
                TokenChange = 0,
                Details = $"PatsKiller Pro {AppVersion} started",
                Metadata = new { app_version = AppVersion, os = Environment.OSVersion.ToString() }
            });
        }

        public void LogAppClose()
        {
            try
            {
                LogActivityAsync(new ActivityLogEntry
                {
                    Action = "app_close",
                    ActionCategory = "app",
                    Success = true,
                    TokenChange = 0,
                    Details = "PatsKiller Pro closed"
                }).Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
        }

        public void LogError(string action, string errorMessage, string? details = null)
        {
            LogActivity(new ActivityLogEntry
            {
                Action = action,
                ActionCategory = "error",
                Success = false,
                TokenChange = 0,
                ErrorMessage = errorMessage,
                Details = details ?? errorMessage
            });
        }
    }

    public class ActivityLogEntry
    {
        public string Action { get; set; } = "";
        public string ActionCategory { get; set; } = "operation";
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
