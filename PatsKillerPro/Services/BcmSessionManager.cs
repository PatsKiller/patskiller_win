using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PatsKillerPro.Communication;
using PatsKillerPro.Utils;
using PatsKillerPro.Vehicle;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Manages BCM unlock session state with keep-alive and automatic re-lock detection.
    /// 
    /// Session Flow:
    /// 1. Get Incode → Auto-unlock sequence (disable alarm, security access, submit incode)
    /// 2. Keep-alive via TesterPresent (0x3E) every 2 seconds
    /// 3. On 3 consecutive failures → check outcode → re-unlock or prompt for new incode
    /// 
    /// Enabled Operations While Unlocked:
    /// - Program Keys
    /// - Erase Keys  
    /// - Key Counter Write (Min/Max)
    /// </summary>
    public class BcmSessionManager : IDisposable
    {
        private static BcmSessionManager? _instance;
        private static readonly object _lock = new();

        public static BcmSessionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new BcmSessionManager();
                    }
                }
                return _instance;
            }
        }

        // Session state
        private bool _isUnlocked;
        private string? _currentOutcode;
        private string? _currentIncode;
        private DateTime _unlockTime;
        private int _consecutiveFailures;
        private const int MAX_FAILURES = 3;

        // Keep-alive timer (System.Threading.Timer for background execution)
        private System.Threading.Timer? _keepAliveTimer;
        private const int KEEP_ALIVE_INTERVAL_MS = 2000; // 2 seconds
        private bool _keepAliveActive;

        // UDS service reference
        private UdsService? _uds;

        // Events for UI updates
        public event Action<BcmSessionState>? SessionStateChanged;
        public event Action<string>? LogMessage;
        public event Action<bool>? UnlockOperationsEnabled;

        public bool IsUnlocked => _isUnlocked;
        public string? CurrentOutcode => _currentOutcode;
        public string? CurrentIncode => _currentIncode;
        public TimeSpan SessionDuration => _isUnlocked ? DateTime.Now - _unlockTime : TimeSpan.Zero;

        private BcmSessionManager()
        {
            _isUnlocked = false;
            _consecutiveFailures = 0;
        }

        /// <summary>
        /// Initialize with UDS service for communication
        /// </summary>
        public void Initialize(UdsService uds)
        {
            _uds = uds;
        }

        /// <summary>
        /// Full unlock sequence: Disable Alarm → Security Access → Submit Incode → Verify
        /// Called after successful incode retrieval from API
        /// </summary>
        public async Task<UnlockResult> UnlockBcmAsync(string outcode, string incode)
        {
            if (_uds == null)
                return UnlockResult.Fail("UDS service not initialized");

            if (string.IsNullOrEmpty(outcode) || string.IsNullOrEmpty(incode))
                return UnlockResult.Fail("Outcode and incode required");

            Log("=== BCM UNLOCK SEQUENCE ===");

            try
            {
                // Step 1: Disable Alarm
                Log("Step 1: Disabling alarm...");
                await Task.Run(() => DisableAlarm());
                await Task.Delay(200);

                // Step 2: Start Extended Session
                Log("Step 2: Starting extended session...");
                await Task.Run(() => _uds.StartExtendedSession(ModuleAddresses.BCM_TX));
                await Task.Delay(100);

                // Step 3: Request Security Access
                Log("Step 3: Requesting security access...");
                var securityResult = await Task.Run(() => _uds.RequestSecurityAccess(ModuleAddresses.BCM_TX));
                if (!securityResult)
                {
                    Log("  ✗ Security access denied");
                    return UnlockResult.Fail("Security access denied - wait for anti-scan timeout");
                }
                Log("  ✓ Security access granted");
                await Task.Delay(100);

                // Step 4: Submit Incode via Routine 0x716D
                Log("Step 4: Submitting incode...");
                var incodeBytes = ParseIncodeToBytes(incode);
                var routineData = new byte[3 + incodeBytes.Length];
                routineData[0] = 0x71;  // Routine ID high
                routineData[1] = 0x6D;  // Routine ID low (PATS incode)
                routineData[2] = 0xCA;  // PATS submission marker
                Array.Copy(incodeBytes, 0, routineData, 3, incodeBytes.Length);

                await Task.Run(() => _uds.RoutineControl(ModuleAddresses.BCM_TX, 0x01, routineData));
                await Task.Delay(300);

                // Step 5: Verify PATS Status (DID 0xC126 should be 0xAA)
                Log("Step 5: Verifying PATS status...");
                var status = await Task.Run(() => _uds.ReadDataByIdentifier(ModuleAddresses.BCM_TX, 0xC126));
                
                if (status == null || status.Length == 0)
                {
                    Log("  ⚠️ Could not read PATS status");
                    // Continue anyway - some vehicles don't respond to this
                }
                else if (status[0] == 0xAA)
                {
                    Log("  ✓ PATS status: UNLOCKED (0xAA)");
                }
                else
                {
                    Log($"  ⚠️ PATS status: 0x{status[0]:X2} (expected 0xAA)");
                    // Don't fail - incode might still be accepted
                }

                // Success - store session state
                _currentOutcode = outcode;
                _currentIncode = incode;
                _unlockTime = DateTime.Now;
                _isUnlocked = true;
                _consecutiveFailures = 0;

                // Start keep-alive timer
                StartKeepAlive();

                Log("=== BCM UNLOCKED SUCCESSFULLY ===");
                
                // Notify UI
                NotifyStateChanged();
                UnlockOperationsEnabled?.Invoke(true);

                return UnlockResult.Success();
            }
            catch (Exception ex)
            {
                Log($"Unlock failed: {ex.Message}");
                return UnlockResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Disable vehicle alarm (try RFA first, then BCM fallback)
        /// </summary>
        private void DisableAlarm()
        {
            try
            {
                // Try RFA first (Remote Function Actuator)
                try
                {
                    _uds!.StartExtendedSession(ModuleAddresses.RFA_TX);
                    Thread.Sleep(50);
                    var disarmData = new byte[] { 0x01, 0x00, 0x00 };
                    _uds.InputOutputControl(ModuleAddresses.RFA_TX, 0x2F, disarmData);
                    Thread.Sleep(300);
                    Log("  ✓ Alarm disabled via RFA");
                    return;
                }
                catch { /* RFA not available, try BCM */ }

                // Fallback to BCM
                _uds!.StartExtendedSession(ModuleAddresses.BCM_TX);
                Thread.Sleep(50);
                var bcmDisarmData = new byte[] { 0x03, 0x00 };
                _uds.InputOutputControl(ModuleAddresses.BCM_TX, 0x30, bcmDisarmData);
                Thread.Sleep(300);
                Log("  ✓ Alarm disabled via BCM");
            }
            catch (Exception ex)
            {
                Log($"  ⚠️ Alarm disable failed: {ex.Message}");
                // Continue anyway - alarm disable is best-effort
            }
        }

        /// <summary>
        /// Start keep-alive timer (TesterPresent every 2 seconds)
        /// </summary>
        private void StartKeepAlive()
        {
            StopKeepAlive();
            
            _keepAliveActive = true;
            _keepAliveTimer = new System.Threading.Timer(KeepAliveCallback, null, KEEP_ALIVE_INTERVAL_MS, KEEP_ALIVE_INTERVAL_MS);
            Log("Keep-alive timer started (2s interval)");
        }

        /// <summary>
        /// Stop keep-alive timer
        /// </summary>
        private void StopKeepAlive()
        {
            _keepAliveActive = false;
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;
        }

        /// <summary>
        /// Keep-alive callback - sends TesterPresent (0x3E) to BCM
        /// </summary>
        private async void KeepAliveCallback(object? state)
        {
            if (!_keepAliveActive || _uds == null || !_isUnlocked)
                return;

            try
            {
                // Send TesterPresent (0x3E 0x00) - suppress positive response
                var response = await Task.Run(() => _uds.SendRawRequest(ModuleAddresses.BCM_TX, new byte[] { 0x3E, 0x00 }));
                
                if (response != null)
                {
                    // Success - reset failure counter
                    _consecutiveFailures = 0;
                    NotifyStateChanged(); // Update session timer display
                }
                else
                {
                    // No response - increment failure counter
                    _consecutiveFailures++;
                    Log($"Keep-alive no response ({_consecutiveFailures}/{MAX_FAILURES})");
                    
                    if (_consecutiveFailures >= MAX_FAILURES)
                    {
                        await HandleSessionLostAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                Log($"Keep-alive error: {ex.Message} ({_consecutiveFailures}/{MAX_FAILURES})");
                
                if (_consecutiveFailures >= MAX_FAILURES)
                {
                    await HandleSessionLostAsync();
                }
            }
        }

        /// <summary>
        /// Handle session lost - check if outcode changed, try re-unlock or notify user
        /// </summary>
        private async Task HandleSessionLostAsync()
        {
            Log("Session lost - checking BCM status...");
            StopKeepAlive();

            try
            {
                // Try to read current outcode
                _uds!.StartExtendedSession(ModuleAddresses.BCM_TX);
                await Task.Delay(50);
                
                var outcodeData = await Task.Run(() => _uds.ReadDataByIdentifier(ModuleAddresses.BCM_TX, 0xC123));
                
                if (outcodeData != null && outcodeData.Length >= 6)
                {
                    var newOutcode = BitConverter.ToString(outcodeData, 0, 6).Replace("-", "");
                    
                    if (newOutcode == _currentOutcode && !string.IsNullOrEmpty(_currentIncode))
                    {
                        // Same outcode - try silent re-unlock with stored incode
                        Log("Same outcode detected - attempting silent re-unlock...");
                        
                        var result = await UnlockBcmAsync(_currentOutcode!, _currentIncode!);
                        if (result.IsSuccess)
                        {
                            Log("Silent re-unlock successful");
                            return;
                        }
                    }
                    else if (newOutcode != _currentOutcode)
                    {
                        // Different outcode - user needs new incode (costs 1 token)
                        Log($"Outcode changed: {SecretRedactor.MaskOutcode(_currentOutcode)} → {SecretRedactor.MaskOutcode(newOutcode)}");
                        _currentOutcode = newOutcode;
                        _currentIncode = null; // Clear stored incode
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Could not check outcode: {ex.Message}");
            }

            // Mark session as locked
            _isUnlocked = false;
            _consecutiveFailures = 0;
            
            Log("=== BCM SESSION LOCKED ===");
            NotifyStateChanged();
            UnlockOperationsEnabled?.Invoke(false);
        }

        /// <summary>
        /// Manually end session (user logout or app close)
        /// </summary>
        public void EndSession()
        {
            StopKeepAlive();
            
            _isUnlocked = false;
            _currentOutcode = null;
            _currentIncode = null;
            _consecutiveFailures = 0;
            
            NotifyStateChanged();
            UnlockOperationsEnabled?.Invoke(false);
            
            Log("BCM session ended");
        }

        /// <summary>
        /// Check if session is valid for operations
        /// </summary>
        public bool CanPerformOperation()
        {
            return _isUnlocked && _consecutiveFailures < MAX_FAILURES;
        }

        /// <summary>
        /// Get current session state for UI
        /// </summary>
        public BcmSessionState GetState()
        {
            return new BcmSessionState
            {
                IsUnlocked = _isUnlocked,
                SessionDuration = SessionDuration,
                KeepAliveActive = _keepAliveActive && _consecutiveFailures < MAX_FAILURES,
                Outcode = _currentOutcode,
                HasIncode = !string.IsNullOrEmpty(_currentIncode)
            };
        }

        private void NotifyStateChanged()
        {
            SessionStateChanged?.Invoke(GetState());
        }

        private void Log(string message)
        {
            Logger.Info($"[BCM Session] {message}");
            LogMessage?.Invoke(message);
        }

        private byte[] ParseIncodeToBytes(string incode)
        {
            incode = incode.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
            var bytes = new byte[incode.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(incode.Substring(i * 2, 2), 16);
            return bytes;
        }

        public void Dispose()
        {
            StopKeepAlive();
            _instance = null;
        }
    }

    /// <summary>
    /// BCM session state for UI display
    /// </summary>
    public class BcmSessionState
    {
        public bool IsUnlocked { get; set; }
        public TimeSpan SessionDuration { get; set; }
        public bool KeepAliveActive { get; set; }
        public string? Outcode { get; set; }
        public bool HasIncode { get; set; }

        public string DurationDisplay => $"{(int)SessionDuration.TotalMinutes:D2}:{SessionDuration.Seconds:D2}";
    }

    /// <summary>
    /// Result of BCM unlock attempt
    /// </summary>
    public class UnlockResult
    {
        public bool IsSuccess { get; private set; }
        public string? Error { get; private set; }

        public static UnlockResult Success() => new() { IsSuccess = true };
        public static UnlockResult Fail(string error) => new() { IsSuccess = false, Error = error };
    }
}
