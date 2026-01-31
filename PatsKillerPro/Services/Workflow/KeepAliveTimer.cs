using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow
{
    /// <summary>
    /// Background timer that sends Tester Present (0x3E 0x00) messages
    /// to maintain ECU diagnostic sessions during multi-step operations.
    /// Supports dual-channel keep-alive for keyless vehicles.
    /// </summary>
    public sealed class KeepAliveTimer : IDisposable
    {
        private readonly Func<uint, byte[], Task<bool>> _sendMessage;
        private readonly Action<string>? _log;
        private CancellationTokenSource? _cts;
        private Task? _timerTask;
        private bool _isRunning;
        private bool _disposed;

        private readonly List<KeepAliveTarget> _targets = new();
        private TimeSpan _interval = TimeSpan.FromMilliseconds(2000);
        private int _failureCount = 0;
        private const int MaxConsecutiveFailures = 3;

        /// <summary>Event fired when keep-alive fails repeatedly</summary>
        public event EventHandler<KeepAliveFailedEventArgs>? KeepAliveFailed;

        /// <summary>Event fired on each successful keep-alive</summary>
        public event EventHandler<KeepAliveSuccessEventArgs>? KeepAliveSuccess;

        /// <summary>Whether the timer is currently running</summary>
        public bool IsRunning => _isRunning;

        /// <summary>Current interval between keep-alive messages</summary>
        public TimeSpan Interval => _interval;

        /// <summary>
        /// Creates a new keep-alive timer.
        /// </summary>
        /// <param name="sendMessage">Function to send UDS message: (moduleAddress, data) => success</param>
        /// <param name="log">Optional logging callback</param>
        public KeepAliveTimer(Func<uint, byte[], Task<bool>> sendMessage, Action<string>? log = null)
        {
            _sendMessage = sendMessage ?? throw new ArgumentNullException(nameof(sendMessage));
            _log = log;
        }

        /// <summary>
        /// Configures keep-alive for a single module.
        /// </summary>
        public void Configure(uint moduleAddress, TimeSpan interval)
        {
            _targets.Clear();
            _targets.Add(new KeepAliveTarget(moduleAddress, CanBusType.HsCan));
            _interval = interval;
        }

        /// <summary>
        /// Configures keep-alive for multiple modules (dual-CAN keyless).
        /// </summary>
        public void Configure(IEnumerable<(uint Module, CanBusType Bus)> targets, TimeSpan interval)
        {
            _targets.Clear();
            foreach (var (module, bus) in targets)
            {
                _targets.Add(new KeepAliveTarget(module, bus));
            }
            _interval = interval;
        }

        /// <summary>
        /// Configures keep-alive from platform pacing configuration.
        /// </summary>
        public void ConfigureFromPlatform(PlatformPacingConfig pacing, PlatformRoutingConfig routing)
        {
            _targets.Clear();
            
            foreach (var (module, bus) in routing.GetKeepAliveTargets())
            {
                _targets.Add(new KeepAliveTarget(module, bus));
            }

            _interval = pacing.EffectiveKeepAliveInterval;
        }

        /// <summary>
        /// Starts the keep-alive timer.
        /// </summary>
        public void Start()
        {
            if (_isRunning || _targets.Count == 0) return;

            _cts = new CancellationTokenSource();
            _isRunning = true;
            _failureCount = 0;

            _log?.Invoke($"Starting keep-alive timer: {_targets.Count} target(s), {_interval.TotalMilliseconds}ms interval");

            _timerTask = RunTimerAsync(_cts.Token);
        }

        /// <summary>
        /// Stops the keep-alive timer.
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning) return;

            _log?.Invoke("Stopping keep-alive timer");

            _cts?.Cancel();
            _isRunning = false;

            if (_timerTask != null)
            {
                try
                {
                    await _timerTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }

            _cts?.Dispose();
            _cts = null;
            _timerTask = null;
        }

        /// <summary>
        /// Stops the keep-alive timer synchronously.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            _log?.Invoke("Stopping keep-alive timer");

            _cts?.Cancel();
            _isRunning = false;

            _cts?.Dispose();
            _cts = null;
            _timerTask = null;
        }

        private async Task RunTimerAsync(CancellationToken ct)
        {
            // Tester Present message: 0x3E 0x00 (with response expected)
            // Some implementations use 0x3E 0x80 to suppress positive response
            var testerPresentMsg = new byte[] { 0x3E, 0x00 };

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_interval, ct).ConfigureAwait(false);

                    foreach (var target in _targets)
                    {
                        ct.ThrowIfCancellationRequested();

                        var success = await _sendMessage(target.ModuleAddress, testerPresentMsg).ConfigureAwait(false);

                        if (success)
                        {
                            _failureCount = 0;
                            KeepAliveSuccess?.Invoke(this, new KeepAliveSuccessEventArgs
                            {
                                ModuleAddress = target.ModuleAddress
                            });
                        }
                        else
                        {
                            _failureCount++;
                            _log?.Invoke($"Keep-alive failed for 0x{target.ModuleAddress:X3} (failure #{_failureCount})");

                            if (_failureCount >= MaxConsecutiveFailures)
                            {
                                _log?.Invoke($"Keep-alive failed {MaxConsecutiveFailures} times - session may be lost");
                                KeepAliveFailed?.Invoke(this, new KeepAliveFailedEventArgs
                                {
                                    ModuleAddress = target.ModuleAddress,
                                    ConsecutiveFailures = _failureCount,
                                    Message = "Session may have expired or vehicle communication lost"
                                });
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Keep-alive error: {ex.Message}");
                    _failureCount++;
                }
            }
        }

        /// <summary>
        /// Sends a single tester present message immediately (outside of timer cycle).
        /// Useful for refreshing session before a critical operation.
        /// </summary>
        public async Task<bool> SendImmediateAsync(uint? targetModule = null)
        {
            var testerPresentMsg = new byte[] { 0x3E, 0x00 };

            if (targetModule.HasValue)
            {
                return await _sendMessage(targetModule.Value, testerPresentMsg).ConfigureAwait(false);
            }

            // Send to all targets
            var allSuccess = true;
            foreach (var target in _targets)
            {
                var success = await _sendMessage(target.ModuleAddress, testerPresentMsg).ConfigureAwait(false);
                allSuccess &= success;
            }
            return allSuccess;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
            _targets.Clear();
        }

        private record KeepAliveTarget(uint ModuleAddress, CanBusType Bus);
    }

    public sealed class KeepAliveFailedEventArgs : EventArgs
    {
        public uint ModuleAddress { get; init; }
        public int ConsecutiveFailures { get; init; }
        public string Message { get; init; } = "";
    }

    public sealed class KeepAliveSuccessEventArgs : EventArgs
    {
        public uint ModuleAddress { get; init; }
    }
}
