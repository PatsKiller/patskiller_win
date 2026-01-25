// ============================================================
// MAINFORM INTEGRATION GUIDE
// Add these snippets to your MainForm.cs (or similar main form)
// ============================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Services;
using PatsKillerPro.Utils;

// ============ ADD THESE FIELDS ============

// UI for promo tokens (add near other UI fields)
private Label? _lblPromoIndicator;  // Shows "+X promo" when user has promo tokens
private ToolTip _tokenToolTip = new ToolTip();

// Periodic balance refresh timer
private System.Windows.Forms.Timer? _balanceRefreshTimer;

// ============ INITIALIZATION ============

/// <summary>
/// Call this in your form constructor or Load event
/// </summary>
private void InitializeTokenServices()
{
    // Subscribe to token balance changes for automatic UI updates
    TokenBalanceService.Instance.BalanceChanged += OnTokenBalanceChanged;
    
    // Start periodic balance refresh (every 60 seconds)
    StartPeriodicBalanceRefresh();
}

/// <summary>
/// Handle token balance changes - updates UI automatically
/// </summary>
private void OnTokenBalanceChanged(object? sender, TokenBalanceChangedEventArgs e)
{
    // Ensure we're on the UI thread
    if (InvokeRequired)
    {
        BeginInvoke(new Action(() => OnTokenBalanceChanged(sender, e)));
        return;
    }
    
    UpdateTokenDisplayUI(e.RegularTokens, e.PromoTokens, e.TotalTokens);
    
    // Log if balance changed significantly
    if (Math.Abs(e.TotalTokens - e.PreviousTotal) > 0)
    {
        Logger.Info($"[MainForm] Token balance updated: {e.PreviousTotal} → {e.TotalTokens}");
    }
}

// ============ UI UPDATES ============

/// <summary>
/// Update the token display in the header
/// </summary>
private void UpdateTokenDisplayUI(int regular, int promo, int total)
{
    if (InvokeRequired)
    {
        BeginInvoke(new Action(() => UpdateTokenDisplayUI(regular, promo, total)));
        return;
    }
    
    // Update main token label (assuming you have _lblTokens or similar)
    // _lblTokens.Text = $"Tokens: {total}";
    
    // Color based on balance
    // _lblTokens.ForeColor = total > 0 
    //     ? Color.FromArgb(76, 175, 80)  // Green
    //     : Color.FromArgb(255, 193, 7); // Yellow/warning
    
    // Show promo indicator if user has promo tokens
    if (_lblPromoIndicator != null)
    {
        if (promo > 0)
        {
            _lblPromoIndicator.Text = $"+{promo} promo";
            _lblPromoIndicator.ForeColor = Color.FromArgb(134, 239, 172); // Light green
            _lblPromoIndicator.Visible = true;
        }
        else
        {
            _lblPromoIndicator.Visible = false;
        }
    }
    
    // Update tooltip to show breakdown
    // _tokenToolTip.SetToolTip(_lblTokens, 
    //     $"Regular: {regular}\nPromo: {promo} (used first)\nTotal: {total}");
}

// ============ PERIODIC REFRESH ============

private void StartPeriodicBalanceRefresh()
{
    _balanceRefreshTimer = new System.Windows.Forms.Timer
    {
        Interval = 60000 // Refresh every 60 seconds
    };
    _balanceRefreshTimer.Tick += async (s, e) =>
    {
        if (!string.IsNullOrEmpty(TokenBalanceService.Instance.AuthToken))
        {
            await TokenBalanceService.Instance.RefreshBalanceAsync();
        }
    };
    _balanceRefreshTimer.Start();
}

private void StopPeriodicBalanceRefresh()
{
    _balanceRefreshTimer?.Stop();
    _balanceRefreshTimer?.Dispose();
    _balanceRefreshTimer = null;
}

// ============ OPERATION EXAMPLES ============

/// <summary>
/// Example: Calculate Incode (costs 1 token)
/// </summary>
private async Task<string?> CalculateIncodeAsync(string outcode, string vin, string? vehicleYear, string? vehicleModel)
{
    var stopwatch = Stopwatch.StartNew();
    bool success = false;
    string? incode = null;
    string? errorMessage = null;
    const int tokenCost = 1;
    
    try
    {
        // 1. Check if user has enough tokens
        if (!TokenBalanceService.Instance.HasEnoughTokens(tokenCost))
        {
            MessageBox.Show(
                $"This operation requires {tokenCost} token(s).\nYou have {TokenBalanceService.Instance.TotalTokens} tokens available.\n\nPurchase more tokens at patskiller.com",
                "Insufficient Tokens",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }
        
        // 2. Deduct tokens (promo used first)
        var deductResult = await TokenBalanceService.Instance.DeductTokensAsync(
            tokenCost,
            "incode_calculation",
            vin,
            vehicleModel);
        
        if (!deductResult.Success)
        {
            errorMessage = deductResult.Error ?? "Token deduction failed";
            throw new Exception(errorMessage);
        }
        
        // Log which tokens were used
        if (deductResult.PromoTokensUsed > 0)
        {
            Logger.Info($"[IncodeCalc] Using {deductResult.PromoTokensUsed} promo + {deductResult.RegularTokensUsed} regular tokens");
        }
        
        // 3. Perform the actual calculation
        // TODO: Replace with your actual incode service call
        // incode = await _incodeService.CalculateAsync(outcode, vin);
        
        // For testing:
        await Task.Delay(500); // Simulate API call
        incode = "01234567890123456789012"; // Fake result
        
        success = true;
        return incode;
    }
    catch (Exception ex)
    {
        success = false;
        errorMessage = ex.Message;
        Logger.Error($"[IncodeCalc] Error: {ex.Message}");
        throw;
    }
    finally
    {
        stopwatch.Stop();
        
        // 4. Log the operation
        ProActivityLogger.Instance.LogIncodeCalculation(
            vin: vin,
            vehicleYear: vehicleYear,
            vehicleModel: vehicleModel,
            success: success,
            tokenChange: success ? -tokenCost : 0,
            responseTimeMs: (int)stopwatch.ElapsedMilliseconds,
            errorMessage: errorMessage);
        
        // 5. Refresh balance to ensure UI is in sync
        TokenBalanceService.Instance.RefreshAfterOperation();
    }
}

/// <summary>
/// Example: Program Key (costs 1 token per session)
/// </summary>
private async Task<bool> ProgramKeyAsync(string vin, string? vehicleYear, string? vehicleModel)
{
    var stopwatch = Stopwatch.StartNew();
    bool success = false;
    string? errorMessage = null;
    const int tokenCost = 1;
    
    try
    {
        if (!TokenBalanceService.Instance.HasEnoughTokens(tokenCost))
        {
            MessageBox.Show(
                $"Key programming requires {tokenCost} token.\nYou have {TokenBalanceService.Instance.TotalTokens} tokens.\n\nPurchase more at patskiller.com",
                "Insufficient Tokens",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }
        
        var deductResult = await TokenBalanceService.Instance.DeductTokensAsync(
            tokenCost,
            "key_programming",
            vin,
            vehicleModel);
        
        if (!deductResult.Success)
        {
            throw new Exception(deductResult.Error ?? "Token deduction failed");
        }
        
        // TODO: Perform actual key programming
        // success = await _j2534Service.ProgramKeyAsync(vin);
        
        success = true;
        return success;
    }
    catch (Exception ex)
    {
        success = false;
        errorMessage = ex.Message;
        return false;
    }
    finally
    {
        stopwatch.Stop();
        
        ProActivityLogger.Instance.LogKeyProgramming(
            vin: vin,
            vehicleYear: vehicleYear,
            vehicleModel: vehicleModel,
            success: success,
            tokenChange: success ? -tokenCost : 0,
            responseTimeMs: (int)stopwatch.ElapsedMilliseconds,
            errorMessage: errorMessage);
        
        TokenBalanceService.Instance.RefreshAfterOperation();
    }
}

/// <summary>
/// Example: Read VIN / Vehicle Detection (FREE - no tokens)
/// </summary>
private async Task<(string? Vin, string? Year, string? Model)?> ReadVinAsync()
{
    var stopwatch = Stopwatch.StartNew();
    bool success = false;
    string? errorMessage = null;
    string? vin = null;
    string? year = null;
    string? model = null;
    
    try
    {
        // Vehicle detection is FREE - no token check needed
        
        // TODO: Perform actual VIN read
        // var result = await _j2534Service.ReadVinAsync();
        
        // For testing:
        await Task.Delay(300);
        vin = "1FA6P8CF5L5123456";
        year = "2021";
        model = "Ford Bronco";
        
        success = true;
        return (vin, year, model);
    }
    catch (Exception ex)
    {
        success = false;
        errorMessage = ex.Message;
        return null;
    }
    finally
    {
        stopwatch.Stop();
        
        // Log detection (no token change)
        ProActivityLogger.Instance.LogVehicleDetection(
            vin: vin ?? "unknown",
            vehicleYear: year,
            vehicleModel: model,
            success: success,
            responseTimeMs: (int)stopwatch.ElapsedMilliseconds,
            errorMessage: errorMessage);
    }
}

/// <summary>
/// Example: Connect J2534 Device (FREE)
/// </summary>
private async Task<bool> ConnectJ2534Async(string deviceName)
{
    try
    {
        // TODO: Perform actual connection
        // var connected = await _j2534Service.ConnectAsync(deviceName);
        
        var connected = true; // For testing
        
        ProActivityLogger.Instance.LogJ2534Connection(deviceName, connected);
        
        return connected;
    }
    catch (Exception ex)
    {
        ProActivityLogger.Instance.LogJ2534Connection(deviceName, false, ex.Message);
        throw;
    }
}

// ============ FORM LIFECYCLE ============

/// <summary>
/// Call when form is closing
/// </summary>
private void OnFormClosing()
{
    try
    {
        // Stop the balance refresh timer
        StopPeriodicBalanceRefresh();
        
        // Unsubscribe from events
        TokenBalanceService.Instance.BalanceChanged -= OnTokenBalanceChanged;
        
        // Log app close
        if (!string.IsNullOrEmpty(ProActivityLogger.Instance.AuthToken))
        {
            ProActivityLogger.Instance.LogAppClose();
        }
    }
    catch { }
}

/// <summary>
/// Call when user logs out
/// </summary>
private void OnLogout()
{
    try
    {
        // Log the logout
        ProActivityLogger.Instance.LogLogout(ProActivityLogger.Instance.UserEmail ?? "unknown");
        
        // Clear service contexts
        ProActivityLogger.Instance.ClearAuthContext();
        TokenBalanceService.Instance.ClearAuthContext();
        
        // Update UI
        UpdateTokenDisplayUI(0, 0, 0);
    }
    catch { }
}

// ============ ERROR LOGGING ============

/// <summary>
/// Log any error that occurs during operations
/// </summary>
private void LogOperationError(string action, Exception ex)
{
    ProActivityLogger.Instance.LogError(
        action: action,
        errorMessage: ex.Message,
        details: ex.ToString());
}

// ============ INTEGRATION CHECKLIST ============
/*
 * 1. Add using PatsKillerPro.Services; at top of file
 * 
 * 2. In constructor or Load:
 *    - Call InitializeTokenServices()
 *    
 * 3. After successful login (in your login success handler):
 *    - TokenBalanceService.Instance.SetAuthContext(token, email);
 *    - ProActivityLogger.Instance.SetAuthContext(token, email);
 *    - await TokenBalanceService.Instance.RefreshBalanceAsync();
 *    
 * 4. In FormClosing event:
 *    - Call OnFormClosing()
 *    
 * 5. In logout handler:
 *    - Call OnLogout()
 *    
 * 6. For each token-consuming operation:
 *    - Check TokenBalanceService.Instance.HasEnoughTokens(cost)
 *    - Call TokenBalanceService.Instance.DeductTokensAsync(...)
 *    - Log with ProActivityLogger.Instance.LogXxx(...)
 *    - Refresh with TokenBalanceService.Instance.RefreshAfterOperation()
 */
