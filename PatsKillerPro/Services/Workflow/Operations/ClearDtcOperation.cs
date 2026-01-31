using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow.Operations
{
    /// <summary>
    /// Clear DTC Operation - Clears diagnostic trouble codes from specified modules.
    /// Token Cost: FREE
    /// </summary>
    public sealed class ClearDtcOperation : OperationBase
    {
        private readonly UdsCommunication _uds;
        private readonly uint[] _targetModules;
        private readonly Action<string>? _log;

        public override string Name => "Clear DTCs";
        public override string Description => "Clears diagnostic trouble codes";
        public override TimeSpan? EstimatedDuration => TimeSpan.FromSeconds(5);
        public override int TokenCost => 0; // FREE
        public override bool RequiresIncode => false;

        public ClearDtcOperation(
            UdsCommunication uds,
            uint[]? targetModules = null,
            PlatformRoutingConfig? routing = null,
            Action<string>? log = null)
        {
            _uds = uds ?? throw new ArgumentNullException(nameof(uds));
            _log = log;
            RoutingConfig = routing ?? PlatformRoutingConfig.Default;

            // Default to BCM, PCM, ABS if no targets specified
            _targetModules = targetModules ?? new uint[]
            {
                RoutingConfig.PrimaryModule,
                RoutingConfig.PcmModule,
                Vehicle.ModuleAddresses.ABS_TX
            };
        }

        public override IReadOnlyList<OperationStep> Steps => BuildSteps();

        private List<OperationStep> BuildSteps()
        {
            var steps = new List<OperationStep>();

            foreach (var module in _targetModules)
            {
                var moduleName = Vehicle.ModuleAddresses.GetModuleName(module);
                var moduleAddr = module; // Capture for closure

                steps.Add(CreateStep(
                    $"Clear {moduleName} DTCs",
                    async ct => await ClearModuleDtcsAsync(moduleAddr, ct),
                    $"Clearing DTCs from {moduleName}",
                    postDelay: TimeSpan.FromMilliseconds(100),
                    retryPolicy: RetryPolicy.Standard,
                    critical: false // Non-critical - continue even if one module fails
                ));
            }

            return steps;
        }

        private async Task ClearModuleDtcsAsync(uint moduleAddress, CancellationToken ct)
        {
            var moduleName = Vehicle.ModuleAddresses.GetModuleName(moduleAddress);
            _log?.Invoke($"Clearing DTCs from {moduleName} (0x{moduleAddress:X3})");

            var response = await _uds.ClearDtcAsync(moduleAddress, 0xFFFFFF, ct);

            if (!response.Success)
            {
                _log?.Invoke($"Warning: Failed to clear {moduleName} DTCs: {response.ErrorMessage}");
                // Don't throw - this is non-critical
            }
            else
            {
                _log?.Invoke($"{moduleName} DTCs cleared");
            }
        }
    }
}
