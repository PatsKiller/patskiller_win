using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Services;

namespace PatsKillerPro
{
    /// <summary>
    /// Partial class to add TokenBalanceService and ProActivityLogger integration
    /// to your existing MainForm. Add this file alongside your MainForm.cs.
    /// 
    /// IMPORTANT: Your MainForm.cs must be declared as: public partial class MainForm : Form
    /// </summary>
    public partial class MainForm
    {
        // ============ KEY SESSION STATE ============
        private string _keySessionVin = "";
        private string _keySessionOutcode = "";

        /// <summary>
        /// Call this in your MainForm constructor AFTER InitializeComponent()
        /// </summary>
        private void InitializeServices()
        {
            // Subscribe to token balance changes
            TokenBalanceService.Instance.BalanceChanged += OnTokenBalanceChanged;
            
            // Log app start
            ProActivityLogger.Instance.LogAppStart();
        }

        /// <summary>
        /// Call this from your MainForm_FormClosing event
        /// </summary>
        private void CleanupServices()
        {
            TokenBalanceService.Instance.BalanceChanged -= OnTokenBalanceChanged;
            TokenBalanceService.Instance.EndKeySession();
            ProActivityLogger.Instance.LogAppClose();
        }

        /// <summary>
        /// Handles token balance changes from the service
        /// </summary>
        private void OnTokenBalanceChanged(object? sender, TokenBalanceChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnTokenBalanceChanged(sender, e)));
                return;
            }

            UpdateTokenDisplayFromService();
        }

        /// <summary>
        /// Updates your UI labels with current token values
        /// Call this after login and whenever balance changes
        /// </summary>
        private void UpdateTokenDisplayFromService()
        {
            var service = TokenBalanceService.Instance;

            // Update your regular token label (adjust name to match your actual label)
            // _lblPurchaseTokens.Text = service.RegularTokens.ToString();
            // _lblPurchaseTokens.Visible = true;

            // Update your promo token label (adjust name to match your actual label)
            // if (service.PromoTokens > 0)
            // {
            //     _lblPromoTokens.Text = $"{service.PromoTokens} promo";
            //     _lblPromoTokens.Visible = true;
            // }
            // else
            // {
            //     _lblPromoTokens.Visible = false;
            // }

            // Or use a combined display:
            // _lblTokens.Text = service.GetDisplayString(); // e.g., "5 (3 + 2 promo)"
        }

        // ============ KEY PROGRAMMING (1 token per session) ============

        /// <summary>
        /// Start a key programming session. Costs 1 token for unlimited keys while same outcode.
        /// Returns true if session started successfully (or was already active).
        /// </summary>
        private async Task<bool> StartKeyProgrammingSessionAsync(string vin, string outcode)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var result = await TokenBalanceService.Instance.StartKeySessionAsync(vin, outcode);

            if (!result.Success)
            {
                MessageBox.Show(result.Error, "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (result.SessionAlreadyActive)
            {
                // Session continues, no charge
                return true;
            }

            // New session started, 1 token charged
            _keySessionVin = vin;
            _keySessionOutcode = outcode;

            ProActivityLogger.Instance.LogKeySessionStart(vin, null, null, outcode, true, -1, (int)stopwatch.ElapsedMilliseconds);
            return true;
        }

        /// <summary>
        /// Check if key operations are free (within active session)
        /// </summary>
        private bool IsKeyOperationFree(string vin, string outcode)
        {
            return TokenBalanceService.Instance.IsKeySessionActive(vin, outcode);
        }

        /// <summary>
        /// Log a key being programmed (FREE within session)
        /// </summary>
        private void LogKeyProgrammed(string vin, string? year, string? model, int keyNumber, bool success, int responseTimeMs, string? error = null)
        {
            ProActivityLogger.Instance.LogKeyProgrammed(vin, year, model, keyNumber, success, responseTimeMs, error);
        }

        /// <summary>
        /// Log all keys erased (FREE within session, or charged if standalone)
        /// </summary>
        private void LogEraseAllKeys(string vin, string? year, string? model, int keysErased, bool success, int tokenChange, int responseTimeMs, string? error = null)
        {
            ProActivityLogger.Instance.LogEraseAllKeys(vin, year, model, keysErased, success, tokenChange, responseTimeMs, error);
        }

        /// <summary>
        /// End the current key session (call when outcode changes, disconnect, or logout)
        /// </summary>
        private void EndKeyProgrammingSession()
        {
            TokenBalanceService.Instance.EndKeySession();
            _keySessionVin = "";
            _keySessionOutcode = "";
        }

        // ============ PARAMETER RESET (1 token per module) ============

        /// <summary>
        /// Deduct 1 token for a parameter reset module
        /// </summary>
        private async Task<TokenDeductResult> DeductForParameterResetModuleAsync(string moduleName, string vin)
        {
            return await TokenBalanceService.Instance.DeductForParamResetAsync(moduleName, vin);
        }

        /// <summary>
        /// Log a parameter reset module completion
        /// </summary>
        private void LogParameterResetModule(string vin, string? year, string? model, string moduleName,
            string outcode, string incode, bool success, int tokenChange, int responseTimeMs, string? error = null)
        {
            ProActivityLogger.Instance.LogParameterResetModule(vin, year, model, moduleName, outcode, incode, success, tokenChange, responseTimeMs, error);
        }

        /// <summary>
        /// Log complete parameter reset operation
        /// </summary>
        private void LogParameterResetComplete(string vin, string? year, string? model,
            int modulesReset, int totalTokens, int responseTimeMs, string[]? modules = null)
        {
            ProActivityLogger.Instance.LogParameterResetComplete(vin, year, model, modulesReset, totalTokens, responseTimeMs, modules);
        }

        // ============ UTILITY OPERATIONS (1 token each) ============

        /// <summary>
        /// Deduct 1 token for a utility operation
        /// </summary>
        private async Task<TokenDeductResult> DeductForUtilityOperationAsync(string operation, string vin)
        {
            return await TokenBalanceService.Instance.DeductForUtilityAsync(operation, vin);
        }

        /// <summary>
        /// Log a utility operation
        /// </summary>
        private void LogUtilityOperation(string operation, string vin, string? year, string? model,
            bool success, int tokenChange, int responseTimeMs, string? error = null)
        {
            ProActivityLogger.Instance.LogUtilityOperation(operation, vin, year, model, success, tokenChange, responseTimeMs, error);
        }

        // ============ FREE OPERATIONS ============

        /// <summary>
        /// Log J2534 device connection (FREE)
        /// </summary>
        private void LogJ2534Connection(string deviceName, bool success, string? error = null)
        {
            ProActivityLogger.Instance.LogJ2534Connection(deviceName, success, error);
        }

        /// <summary>
        /// Log J2534 device disconnection (FREE)
        /// </summary>
        private void LogJ2534Disconnect(string deviceName)
        {
            ProActivityLogger.Instance.LogJ2534Disconnect(deviceName);
        }

        /// <summary>
        /// Log vehicle detection (FREE)
        /// </summary>
        private void LogVehicleDetection(string vin, string? year, string? model, bool success, int responseTimeMs, string? error = null)
        {
            ProActivityLogger.Instance.LogVehicleDetection(vin, year, model, success, responseTimeMs, error);
        }

        // ============ AUTH ============

        /// <summary>
        /// Log user login
        /// </summary>
        private void LogLogin(string email, bool success, string? errorMessage = null, int responseTimeMs = 0)
        {
            ProActivityLogger.Instance.LogLogin(email, success, errorMessage, responseTimeMs);
        }

        /// <summary>
        /// Log user logout
        /// </summary>
        private void LogLogout(string email)
        {
            ProActivityLogger.Instance.LogLogout(email);
        }

        // ============ HELPERS ============

        /// <summary>
        /// Check if user has enough tokens
        /// </summary>
        private bool HasEnoughTokens(int required)
        {
            return TokenBalanceService.Instance.HasEnoughTokens(required);
        }

        /// <summary>
        /// Refresh token balance after an operation
        /// </summary>
        private void RefreshTokenBalanceAfterOperation(int delayMs = 500)
        {
            TokenBalanceService.Instance.RefreshAfterOperation(delayMs);
        }

        /// <summary>
        /// Get total tokens (regular + promo)
        /// </summary>
        private int GetTotalTokens()
        {
            return TokenBalanceService.Instance.TotalTokens;
        }

        /// <summary>
        /// Get regular tokens only
        /// </summary>
        private int GetRegularTokens()
        {
            return TokenBalanceService.Instance.RegularTokens;
        }

        /// <summary>
        /// Get promo tokens only
        /// </summary>
        private int GetPromoTokens()
        {
            return TokenBalanceService.Instance.PromoTokens;
        }
    }
}
