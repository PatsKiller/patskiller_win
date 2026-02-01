using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Manages session state persistence to file for recovery after disconnect/restart.
    /// </summary>
    public class SessionStateManager
    {
        private static readonly string StateFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PatsKillerPro", "session_state.json");

        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PatsKillerPro", "Logs");

        /// <summary>
        /// Session state data structure
        /// </summary>
        public class SessionState
        {
            public string? VIN { get; set; }
            public string? Platform { get; set; }
            public string? Outcode { get; set; }
            public string? Incode { get; set; }
            public int KeyCount { get; set; }
            public bool IsBcmUnlocked { get; set; }
            public bool IsGatewayUnlocked { get; set; }
            public DateTime? SessionStartTime { get; set; }
            public DateTime? SessionExpiryTime { get; set; }
            public DateTime LastSaved { get; set; }
            public string? DeviceName { get; set; }
        }

        private SessionState _currentState = new();

        public SessionState CurrentState => _currentState;

        /// <summary>
        /// Update session state from VehicleSession
        /// </summary>
        public void UpdateFromVehicleSession(Workflow.VehicleSession session, string? vin = null, string? platform = null)
        {
            _currentState.IsBcmUnlocked = session.HasActiveSecuritySession;
            _currentState.SessionExpiryTime = session.HasActiveSecuritySession 
                ? DateTime.Now.Add(session.SecurityTimeRemaining ?? TimeSpan.Zero) 
                : null;
            
            if (!string.IsNullOrEmpty(vin)) _currentState.VIN = vin;
            if (!string.IsNullOrEmpty(platform)) _currentState.Platform = platform;
            
            Save();
        }

        /// <summary>
        /// Update codes
        /// </summary>
        public void UpdateCodes(string? outcode, string? incode)
        {
            if (!string.IsNullOrEmpty(outcode)) _currentState.Outcode = outcode;
            if (!string.IsNullOrEmpty(incode)) _currentState.Incode = incode;
            Save();
        }

        /// <summary>
        /// Update key count
        /// </summary>
        public void UpdateKeyCount(int count)
        {
            _currentState.KeyCount = count;
            Save();
        }

        /// <summary>
        /// Start a new session
        /// </summary>
        public void StartSession(string deviceName)
        {
            _currentState.SessionStartTime = DateTime.Now;
            _currentState.DeviceName = deviceName;
            Save();
        }

        /// <summary>
        /// Clear session (on disconnect or close)
        /// </summary>
        public void ClearSession()
        {
            _currentState = new SessionState();
            Save();
        }

        /// <summary>
        /// Save current state to file
        /// </summary>
        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(StateFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                _currentState.LastSaved = DateTime.Now;
                var json = JsonSerializer.Serialize(_currentState, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(StateFilePath, json);
            }
            catch { /* Silent fail - state persistence is not critical */ }
        }

        /// <summary>
        /// Load state from file
        /// </summary>
        public bool Load()
        {
            try
            {
                if (!File.Exists(StateFilePath)) return false;

                var json = File.ReadAllText(StateFilePath);
                var state = JsonSerializer.Deserialize<SessionState>(json);
                
                if (state != null)
                {
                    _currentState = state;
                    
                    // Check if session is still valid (not expired)
                    if (_currentState.SessionExpiryTime.HasValue && 
                        _currentState.SessionExpiryTime.Value < DateTime.Now)
                    {
                        // Session expired - clear security state
                        _currentState.IsBcmUnlocked = false;
                        _currentState.IsGatewayUnlocked = false;
                        _currentState.SessionExpiryTime = null;
                    }
                    
                    return true;
                }
            }
            catch { /* Silent fail */ }
            
            return false;
        }

        /// <summary>
        /// Check if there's a recoverable session
        /// </summary>
        public bool HasRecoverableSession()
        {
            return !string.IsNullOrEmpty(_currentState.VIN) && 
                   !string.IsNullOrEmpty(_currentState.Incode);
        }

        /// <summary>
        /// Get time remaining on session
        /// </summary>
        public TimeSpan? GetTimeRemaining()
        {
            if (!_currentState.SessionExpiryTime.HasValue) return null;
            var remaining = _currentState.SessionExpiryTime.Value - DateTime.Now;
            return remaining > TimeSpan.Zero ? remaining : null;
        }

        #region Activity Log Auto-Save

        /// <summary>
        /// Save activity log to file
        /// </summary>
        public static void SaveLog(string logContent, string? vin = null)
        {
            try
            {
                if (!Directory.Exists(LogFolder))
                    Directory.CreateDirectory(LogFolder);

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var vinPart = !string.IsNullOrEmpty(vin) ? $"_{vin}" : "";
                var fileName = $"PatsKiller_Log_{timestamp}{vinPart}.txt";
                var filePath = Path.Combine(LogFolder, fileName);

                File.WriteAllText(filePath, logContent);
            }
            catch { /* Silent fail */ }
        }

        /// <summary>
        /// Get log folder path
        /// </summary>
        public static string GetLogFolder() => LogFolder;

        /// <summary>
        /// Clean up old log files (keep last 50)
        /// </summary>
        public static void CleanupOldLogs(int keepCount = 50)
        {
            try
            {
                if (!Directory.Exists(LogFolder)) return;

                var files = Directory.GetFiles(LogFolder, "PatsKiller_Log_*.txt")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .Skip(keepCount)
                    .ToArray();

                foreach (var file in files)
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch { /* Silent fail */ }
        }

        #endregion
    }
}
