using System;
using System.Threading;
using System.Threading.Tasks;
using PatsKillerPro.Services.Workflow;
using PatsKillerPro.Services.Workflow.Operations;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Extension methods to integrate the EZimmo workflow system with J2534Service.
    /// Provides workflow-based alternatives to the existing direct operations.
    /// </summary>
    public static class J2534ServiceExtensions
    {
        private static WorkflowService? _workflowService;
        private static readonly object _initLock = new();

        /// <summary>
        /// Gets or creates the workflow service instance.
        /// </summary>
        public static WorkflowService GetWorkflowService(this J2534Service service)
        {
            if (_workflowService != null) return _workflowService;

            lock (_initLock)
            {
                if (_workflowService == null)
                {
                    _workflowService = new WorkflowService(service.Log);
                }
                return _workflowService;
            }
        }

        /// <summary>
        /// Configures the workflow service with the current connection.
        /// Call this after connecting to a device.
        /// </summary>
        public static void ConfigureWorkflow(
            this J2534Service service,
            Func<uint, byte[], Task<UdsResponse>> sendUdsAsync,
            string? platformCode = null,
            string? vin = null)
        {
            var workflow = service.GetWorkflowService();
            workflow.Configure(sendUdsAsync, platformCode, vin);
        }

        /// <summary>
        /// Programs a key using the EZimmo workflow system.
        /// Provides proper state machine, timing, NRC handling, and verification.
        /// </summary>
        public static async Task<J2534Service.KeyOperationResult> ProgramKeyWithWorkflowAsync(
            this J2534Service service,
            string incode,
            int slot,
            CancellationToken ct = default)
        {
            var workflow = service.GetWorkflowService();

            try
            {
                var result = await workflow.ProgramKeyAsync(incode, slot, ct);

                if (result.Success)
                {
                    // Get final key count from operation result
                    var keyCount = 0;
                    if (result.ResultData is ProgramKeyOperation op)
                    {
                        keyCount = op.FinalKeyCount;
                    }
                    else
                    {
                        // Read key count directly
                        var kcResult = await workflow.ReadKeyCountAsync(ct);
                        keyCount = kcResult.Success ? kcResult.KeyCount : slot;
                    }

                    return J2534Service.KeyOperationResult.Ok(keyCount);
                }

                return J2534Service.KeyOperationResult.Fail(result.ErrorMessage ?? "Key programming failed");
            }
            catch (OperationCanceledException)
            {
                return J2534Service.KeyOperationResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                return J2534Service.KeyOperationResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Erases all keys using the EZimmo workflow system.
        /// </summary>
        public static async Task<J2534Service.KeyOperationResult> EraseAllKeysWithWorkflowAsync(
            this J2534Service service,
            string incode,
            CancellationToken ct = default)
        {
            var workflow = service.GetWorkflowService();

            try
            {
                var result = await workflow.EraseAllKeysAsync(incode, ct);

                if (result.Success)
                {
                    return J2534Service.KeyOperationResult.Ok(0);
                }

                return J2534Service.KeyOperationResult.Fail(result.ErrorMessage ?? "Key erase failed");
            }
            catch (OperationCanceledException)
            {
                return J2534Service.KeyOperationResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                return J2534Service.KeyOperationResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Reads outcode using the workflow system.
        /// </summary>
        public static async Task<J2534Service.OutcodeResult> ReadOutcodeWithWorkflowAsync(
            this J2534Service service,
            CancellationToken ct = default)
        {
            var workflow = service.GetWorkflowService();

            try
            {
                var result = await workflow.ReadOutcodeAsync(ct);

                return result.Success
                    ? J2534Service.OutcodeResult.Ok(result.Outcode!)
                    : J2534Service.OutcodeResult.Fail(result.Error ?? "Failed to read outcode");
            }
            catch (Exception ex)
            {
                return J2534Service.OutcodeResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Reads key count using the workflow system.
        /// </summary>
        public static async Task<J2534Service.KeyCountResult> ReadKeyCountWithWorkflowAsync(
            this J2534Service service,
            CancellationToken ct = default)
        {
            var workflow = service.GetWorkflowService();

            try
            {
                var result = await workflow.ReadKeyCountAsync(ct);

                return result.Success
                    ? J2534Service.KeyCountResult.Ok(result.KeyCount)
                    : J2534Service.KeyCountResult.Fail(result.Error ?? "Failed to read key count");
            }
            catch (Exception ex)
            {
                return J2534Service.KeyCountResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Clears DTCs using the workflow system.
        /// </summary>
        public static async Task<J2534Service.OperationResult> ClearDtcsWithWorkflowAsync(
            this J2534Service service,
            CancellationToken ct = default)
        {
            var workflow = service.GetWorkflowService();

            try
            {
                var result = await workflow.ClearDtcsAsync(null, ct);

                return result.Success
                    ? J2534Service.OperationResult.Ok()
                    : J2534Service.OperationResult.Fail(result.Error ?? "Failed to clear DTCs");
            }
            catch (Exception ex)
            {
                return J2534Service.OperationResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Subscribes to workflow progress events.
        /// </summary>
        public static void SubscribeToWorkflowProgress(
            this J2534Service service,
            EventHandler<OperationProgressEventArgs> handler)
        {
            service.GetWorkflowService().ProgressUpdated += handler;
        }

        /// <summary>
        /// Subscribes to workflow error events.
        /// </summary>
        public static void SubscribeToWorkflowErrors(
            this J2534Service service,
            EventHandler<OperationErrorEventArgs> handler)
        {
            service.GetWorkflowService().ErrorOccurred += handler;
        }

        /// <summary>
        /// Subscribes to user action required events.
        /// </summary>
        public static void SubscribeToUserActionRequired(
            this J2534Service service,
            EventHandler<UserActionRequiredEventArgs> handler)
        {
            service.GetWorkflowService().UserActionRequired += handler;
        }

        /// <summary>
        /// Subscribes to operation completion events.
        /// </summary>
        public static void SubscribeToOperationComplete(
            this J2534Service service,
            EventHandler<OperationCompleteEventArgs> handler)
        {
            service.GetWorkflowService().OperationCompleted += handler;
        }

        /// <summary>
        /// Resumes the workflow after a user action.
        /// </summary>
        public static void ResumeWorkflowAfterUserAction(this J2534Service service, bool success = true)
        {
            service.GetWorkflowService().ResumeAfterUserAction(success);
        }

        /// <summary>
        /// Cancels the current workflow operation.
        /// </summary>
        public static void CancelWorkflowOperation(this J2534Service service)
        {
            service.GetWorkflowService().CancelCurrentOperation();
        }

        /// <summary>
        /// Gets whether the workflow service is currently busy.
        /// </summary>
        public static bool IsWorkflowBusy(this J2534Service service)
        {
            return _workflowService?.IsBusy ?? false;
        }

        /// <summary>
        /// Gets the current workflow operation state.
        /// </summary>
        public static OperationState GetWorkflowState(this J2534Service service)
        {
            return _workflowService?.State ?? OperationState.Idle;
        }

        /// <summary>
        /// Gets remaining security session time.
        /// </summary>
        public static TimeSpan? GetSecurityTimeRemaining(this J2534Service service)
        {
            return _workflowService?.SecurityTimeRemaining;
        }

        /// <summary>
        /// Disposes the workflow service.
        /// </summary>
        public static void DisposeWorkflowService(this J2534Service service)
        {
            lock (_initLock)
            {
                _workflowService?.Dispose();
                _workflowService = null;
            }
        }
    }
}
