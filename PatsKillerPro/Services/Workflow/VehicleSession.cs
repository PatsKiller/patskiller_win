using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow
{
    /// <summary>
    /// Manages vehicle diagnostic/security session state.
    /// Tracks when security was unlocked, session validity, and provides
    /// warnings before session expiration.
    /// </summary>
    public sealed class VehicleSession : IDisposable
    {
        private readonly object _lock = new();
        private readonly Dictionary<uint, ModuleSessionInfo> _moduleSessions = new();
        private readonly Action<string>? _log;
        private System.Threading.Timer? _expirationTimer;
        private bool _disposed;

        /// <summary>Event fired when security session is about to expire</summary>
        public event EventHandler<SessionExpiringEventArgs>? SessionExpiring;

        /// <summary>Event fired when security session has expired</summary>
        public event EventHandler<SessionExpiredEventArgs>? SessionExpired;

        /// <summary>Event fired when security is successfully unlocked</summary>
        public event EventHandler<SecurityUnlockedEventArgs>? SecurityUnlocked;

        /// <summary>Default session duration (15 minutes)</summary>
        public static readonly TimeSpan DefaultSessionDuration = TimeSpan.FromMinutes(15);

        /// <summary>Warning threshold before expiration (2 minutes)</summary>
        public static readonly TimeSpan DefaultWarningThreshold = TimeSpan.FromMinutes(2);

        /// <summary>Current platform pacing configuration</summary>
        public PlatformPacingConfig? PacingConfig { get; private set; }

        /// <summary>Current platform routing configuration</summary>
        public PlatformRoutingConfig? RoutingConfig { get; private set; }

        /// <summary>Vehicle VIN if known</summary>
        public string? Vin { get; private set; }

        /// <summary>Whether any module has an active security session</summary>
        public bool HasActiveSecuritySession
        {
            get
            {
                lock (_lock)
                {
                    foreach (var session in _moduleSessions.Values)
                    {
                        if (session.IsSecurityUnlocked && !session.IsExpired)
                            return true;
                    }
                    return false;
                }
            }
        }

        /// <summary>Time remaining on the primary security session</summary>
        public TimeSpan? SecurityTimeRemaining
        {
            get
            {
                lock (_lock)
                {
                    foreach (var session in _moduleSessions.Values)
                    {
                        if (session.IsSecurityUnlocked && !session.IsExpired)
                            return session.TimeRemaining;
                    }
                    return null;
                }
            }
        }

        public VehicleSession(Action<string>? log = null)
        {
            _log = log;
            StartExpirationTimer();
        }

        /// <summary>
        /// Configures the session with platform settings.
        /// </summary>
        public void Configure(PlatformPacingConfig pacing, PlatformRoutingConfig routing, string? vin = null)
        {
            PacingConfig = pacing;
            RoutingConfig = routing;
            Vin = vin;

            _log?.Invoke($"Session configured: Platform={routing.PlatformCode}, VIN={vin ?? "unknown"}");
        }

        /// <summary>
        /// Records that a diagnostic session was started on a module.
        /// </summary>
        public void RecordDiagnosticSession(uint moduleAddress, DiagnosticSessionType sessionType)
        {
            lock (_lock)
            {
                if (!_moduleSessions.TryGetValue(moduleAddress, out var session))
                {
                    session = new ModuleSessionInfo(moduleAddress);
                    _moduleSessions[moduleAddress] = session;
                }

                session.DiagnosticSessionType = sessionType;
                session.DiagnosticSessionStarted = DateTime.UtcNow;

                _log?.Invoke($"Diagnostic session started: Module=0x{moduleAddress:X3}, Type={sessionType}");
            }
        }

        /// <summary>
        /// Records that security was successfully unlocked on a module.
        /// </summary>
        public void RecordSecurityUnlock(uint moduleAddress, TimeSpan? customDuration = null)
        {
            var duration = customDuration ?? PacingConfig?.SecuritySessionDuration ?? DefaultSessionDuration;

            lock (_lock)
            {
                if (!_moduleSessions.TryGetValue(moduleAddress, out var session))
                {
                    session = new ModuleSessionInfo(moduleAddress);
                    _moduleSessions[moduleAddress] = session;
                }

                session.IsSecurityUnlocked = true;
                session.SecurityUnlockTime = DateTime.UtcNow;
                session.SecuritySessionDuration = duration;
                session.WarningFired = false;
                session.ExpiredFired = false;

                _log?.Invoke($"Security unlocked: Module=0x{moduleAddress:X3}, Duration={duration.TotalMinutes:F1} min");
            }

            SecurityUnlocked?.Invoke(this, new SecurityUnlockedEventArgs
            {
                ModuleAddress = moduleAddress,
                SessionDuration = duration
            });
        }

        /// <summary>
        /// Clears security state for a module (e.g., after session end or failure).
        /// </summary>
        public void ClearSecurityState(uint moduleAddress)
        {
            lock (_lock)
            {
                if (_moduleSessions.TryGetValue(moduleAddress, out var session))
                {
                    session.IsSecurityUnlocked = false;
                    session.SecurityUnlockTime = null;
                    _log?.Invoke($"Security state cleared: Module=0x{moduleAddress:X3}");
                }
            }
        }

        /// <summary>
        /// Clears all session state.
        /// </summary>
        public void ClearAll()
        {
            lock (_lock)
            {
                _moduleSessions.Clear();
                _log?.Invoke("All session state cleared");
            }
        }

        /// <summary>
        /// Checks if security is currently unlocked for a specific module.
        /// </summary>
        public bool IsSecurityUnlocked(uint moduleAddress)
        {
            lock (_lock)
            {
                return _moduleSessions.TryGetValue(moduleAddress, out var session) &&
                       session.IsSecurityUnlocked &&
                       !session.IsExpired;
            }
        }

        /// <summary>
        /// Gets time remaining on security session for a module.
        /// </summary>
        public TimeSpan? GetTimeRemaining(uint moduleAddress)
        {
            lock (_lock)
            {
                if (_moduleSessions.TryGetValue(moduleAddress, out var session) &&
                    session.IsSecurityUnlocked &&
                    !session.IsExpired)
                {
                    return session.TimeRemaining;
                }
                return null;
            }
        }

        /// <summary>
        /// Checks if there's enough time remaining for an operation.
        /// </summary>
        public bool HasSufficientTimeForOperation(uint moduleAddress, TimeSpan estimatedDuration)
        {
            var remaining = GetTimeRemaining(moduleAddress);
            if (!remaining.HasValue) return false;

            // Add buffer time (1 minute) to be safe
            return remaining.Value > (estimatedDuration + TimeSpan.FromMinutes(1));
        }

        private void StartExpirationTimer()
        {
            _expirationTimer = new System.Threading.Timer(CheckExpiration, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        private void CheckExpiration(object? state)
        {
            if (_disposed) return;

            var warningThreshold = PacingConfig?.SecuritySessionWarningThreshold ?? DefaultWarningThreshold;

            lock (_lock)
            {
                foreach (var kvp in _moduleSessions)
                {
                    var session = kvp.Value;
                    if (!session.IsSecurityUnlocked) continue;

                    var remaining = session.TimeRemaining;
                    if (!remaining.HasValue) continue;

                    // Check for expiration
                    if (session.IsExpired && !session.ExpiredFired)
                    {
                        session.ExpiredFired = true;
                        session.IsSecurityUnlocked = false;

                        _log?.Invoke($"Security session EXPIRED: Module=0x{session.ModuleAddress:X3}");

                        Task.Run(() => SessionExpired?.Invoke(this, new SessionExpiredEventArgs
                        {
                            ModuleAddress = session.ModuleAddress
                        }));
                    }
                    // Check for warning threshold
                    else if (remaining.Value <= warningThreshold && !session.WarningFired)
                    {
                        session.WarningFired = true;

                        _log?.Invoke($"Security session expiring soon: Module=0x{session.ModuleAddress:X3}, {remaining.Value.TotalMinutes:F1} min remaining");

                        Task.Run(() => SessionExpiring?.Invoke(this, new SessionExpiringEventArgs
                        {
                            ModuleAddress = session.ModuleAddress,
                            TimeRemaining = remaining.Value
                        }));
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _expirationTimer?.Dispose();
            _moduleSessions.Clear();
        }

        private class ModuleSessionInfo
        {
            public uint ModuleAddress { get; }
            public DiagnosticSessionType DiagnosticSessionType { get; set; }
            public DateTime? DiagnosticSessionStarted { get; set; }
            public bool IsSecurityUnlocked { get; set; }
            public DateTime? SecurityUnlockTime { get; set; }
            public TimeSpan SecuritySessionDuration { get; set; } = DefaultSessionDuration;
            public bool WarningFired { get; set; }
            public bool ExpiredFired { get; set; }

            public ModuleSessionInfo(uint moduleAddress)
            {
                ModuleAddress = moduleAddress;
            }

            public bool IsExpired
            {
                get
                {
                    if (!IsSecurityUnlocked || !SecurityUnlockTime.HasValue) return true;
                    return DateTime.UtcNow - SecurityUnlockTime.Value >= SecuritySessionDuration;
                }
            }

            public TimeSpan? TimeRemaining
            {
                get
                {
                    if (!IsSecurityUnlocked || !SecurityUnlockTime.HasValue) return null;
                    var elapsed = DateTime.UtcNow - SecurityUnlockTime.Value;
                    var remaining = SecuritySessionDuration - elapsed;
                    return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
                }
            }
        }
    }

    /// <summary>
    /// Types of UDS diagnostic sessions
    /// </summary>
    public enum DiagnosticSessionType
    {
        Default = 0x01,
        Programming = 0x02,
        Extended = 0x03
    }

    public sealed class SessionExpiringEventArgs : EventArgs
    {
        public uint ModuleAddress { get; init; }
        public TimeSpan TimeRemaining { get; init; }
    }

    public sealed class SessionExpiredEventArgs : EventArgs
    {
        public uint ModuleAddress { get; init; }
    }

    public sealed class SecurityUnlockedEventArgs : EventArgs
    {
        public uint ModuleAddress { get; init; }
        public TimeSpan SessionDuration { get; init; }
    }
}
