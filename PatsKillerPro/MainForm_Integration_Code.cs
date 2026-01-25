// ============================================================
// MAINFORM INTEGRATION GUIDE
// Add these snippets to your MainForm.cs
// ============================================================

// ============ 1. INITIALIZATION (in constructor or Load event) ============

private void InitializeServices()
{
    // Subscribe to token balance changes
    TokenBalanceService.Instance.BalanceChanged += OnTokenBalanceChanged;
}

private void OnTokenBalanceChanged(object? sender, TokenBalanceChangedEventArgs e)
{
    // Update UI on main thread
    if (InvokeRequired)
    {
        BeginInvoke(new Action(() => OnTokenBalanceChanged(sender, e)));
        return;
    }
    
    // Update header token display with promo support
    if (e.PromoTokens > 0)
    {
        _lblTokens.Text = $"Tokens: {e.TotalTokens}";
        _lblTokens.ForeColor = Color.FromArgb(76, 175, 80); // Green
        
        // Show promo indicator (optional tooltip or subtitle)
        toolTip1.SetToolTip(_lblTokens, $"Regular: {e.RegularTokens} | Promo: {e.PromoTokens}");
    }
    else
    {
        _lblTokens.Text = $"Tokens: {e.TotalTokens}";
        _lblTokens.ForeColor = e.TotalTokens > 0 
            ? Color.FromArgb(76, 175, 80)  // Green
            : Color.FromArgb(255, 193, 7); // Yellow/warning
    }
}

// ============ 2. AFTER LOGIN SUCCESS ============

private async void OnLoginSuccess(string authToken, string userEmail, int initialTokens)
{
    // Set up services with auth context
    ProActivityLogger.Instance.AuthToken = authToken;
    ProActivityLogger.Instance.UserEmail = userEmail;
    
    TokenBalanceService.Instance.AuthToken = authToken;
    TokenBalanceService.Instance.UserEmail = userEmail;
    
    // Log the login
    ProActivityLogger.Instance.LogLogin(userEmail, success: true);
    
    // Log app start (if not already done)
    ProActivityLogger.Instance.LogAppStart();
    
    // Refresh to get accurate balance including promo tokens
    await TokenBalanceService.Instance.RefreshBalanceAsync();
    
    // Update UI
    UpdateTokenDisplay();
}

// ============ 3. LOGOUT ============

private void OnLogout()
{
    // Log the logout
    ProActivityLogger.Instance.LogLogout(_userEmail);
    
    // Clear service state
    ProActivityLogger.Instance.AuthToken = null;
    TokenBalanceService.Instance.AuthToken = null;
}

// ============ 4. INCODE CALCULATION ============

private async Task<string?> CalculateIncodeAsync(string outcode, string vin, string vehicleYear, string vehicleModel)
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    string? incode = null;
    bool success = false;
    string? errorMessage = null;
    int tokenCost = 1; // Or get from config
    
    try
    {
        // Check tokens first
        if (!TokenBalanceService.Instance.HasEnoughTokens(tokenCost))
        {
            errorMessage = "Insufficient tokens";
            throw new Exception(errorMessage);
        }
        
        // Deduct tokens BEFORE operation (promo used first)
        var deductResult = await TokenBalanceService.Instance.DeductTokensAsync(
            tokenCost, 
            "incode_calculation",
            vin,
            vehicleModel
        );
        
        if (!deductResult.Success)
        {
            errorMessage = deductResult.Error;
            throw new Exception(errorMessage);
        }
        
        // Perform the actual calculation
        incode = await _incodeService.CalculateIncodeAsync(outcode, vin);
        success = true;
        
        return incode;
    }
    catch (Exception ex)
    {
        success = false;
        errorMessage = ex.Message;
        
        // TODO: Consider refunding tokens on failure
        throw;
    }
    finally
    {
        stopwatch.Stop();
        
        // Log the operation
        ProActivityLogger.Instance.LogIncodeCalculation(
            vin: vin,
            vehicleYear: vehicleYear,
            vehicleModel: vehicleModel,
            success: success,
            tokenChange: success ? -tokenCost : 0,
            responseTimeMs: (int)stopwatch.ElapsedMilliseconds,
            errorMessage: errorMessage
        );
        
        // Refresh balance (fire and forget)
        TokenBalanceService.Instance.RefreshAfterOperation();
    }
}

// ============ 5. KEY PROGRAMMING ============

private async Task<bool> ProgramKeyAsync(string vin, string vehicleYear, string vehicleModel)
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    bool success = false;
    string? errorMessage = null;
    int tokenCost = 1; // Key programming cost
    
    try
    {
        // Check tokens
        if (!TokenBalanceService.Instance.HasEnoughTokens(tokenCost))
        {
            MessageBox.Show("Insufficient tokens. Please purchase more tokens.", "Tokens Required", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        
        // Deduct tokens
        var deductResult = await TokenBalanceService.Instance.DeductTokensAsync(
            tokenCost,
            "key_programming",
            vin,
            vehicleModel
        );
        
        if (!deductResult.Success)
        {
            throw new Exception(deductResult.Error);
        }
        
        // Log which tokens were used
        Logger.Info($"[KeyProgramming] Using {deductResult.PromoTokensUsed} promo + {deductResult.RegularTokensUsed} regular tokens");
        
        // Perform key programming...
        success = await _j2534Service.ProgramKeyAsync(vin);
        
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
        
        // Log the operation
        ProActivityLogger.Instance.LogKeyProgramming(
            vin: vin,
            vehicleYear: vehicleYear,
            vehicleModel: vehicleModel,
            success: success,
            tokenChange: success ? -tokenCost : 0,
            responseTimeMs: (int)stopwatch.ElapsedMilliseconds,
            errorMessage: errorMessage
        );
        
        // Always refresh balance after operation
        TokenBalanceService.Instance.RefreshAfterOperation();
    }
}

// ============ 6. ERASE ALL KEYS ============

private async Task<bool> EraseAllKeysAsync(string vin, string vehicleYear, string vehicleModel)
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    bool success = false;
    string? errorMessage = null;
    int tokenCost = 2; // Erase keys might cost more
    
    // ... similar pattern to ProgramKeyAsync ...
    
    try
    {
        if (!TokenBalanceService.Instance.HasEnoughTokens(tokenCost))
        {
            MessageBox.Show($"This operation requires {tokenCost} tokens. You have {TokenBalanceService.Instance.TotalTokens}.", 
                "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        
        var deductResult = await TokenBalanceService.Instance.DeductTokensAsync(tokenCost, "erase_all_keys", vin, vehicleModel);
        if (!deductResult.Success) throw new Exception(deductResult.Error);
        
        success = await _j2534Service.EraseAllKeysAsync(vin);
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
        ProActivityLogger.Instance.LogEraseAllKeys(vin, vehicleYear, vehicleModel, success, success ? -tokenCost : 0, (int)stopwatch.ElapsedMilliseconds, errorMessage);
        TokenBalanceService.Instance.RefreshAfterOperation();
    }
}

// ============ 7. READ VIN / VEHICLE DETECTION ============

private async Task<VehicleInfo?> ReadVinAsync()
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    bool success = false;
    string? errorMessage = null;
    string? vin = null;
    string? year = null;
    string? model = null;
    
    try
    {
        // Vehicle detection is FREE - no token cost
        var result = await _j2534Service.ReadVinAsync();
        
        if (result != null)
        {
            vin = result.Vin;
            year = result.Year;
            model = result.Model;
            success = true;
        }
        
        return result;
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
            errorMessage: errorMessage
        );
    }
}

// ============ 8. J2534 DEVICE CONNECTION ============

private async Task<bool> ConnectJ2534DeviceAsync(string deviceName)
{
    try
    {
        var connected = await _j2534Service.ConnectAsync(deviceName);
        
        // Log connection
        ProActivityLogger.Instance.LogJ2534Connection(deviceName, connected);
        
        return connected;
    }
    catch (Exception ex)
    {
        ProActivityLogger.Instance.LogJ2534Connection(deviceName, false, ex.Message);
        throw;
    }
}

// ============ 9. UPDATE TOKEN DISPLAY HELPER ============

private void UpdateTokenDisplay()
{
    if (InvokeRequired)
    {
        BeginInvoke(new Action(UpdateTokenDisplay));
        return;
    }
    
    var service = TokenBalanceService.Instance;
    
    // Main token count
    _lblTokens.Text = $"Tokens: {service.TotalTokens}";
    _lblTokens.ForeColor = service.TotalTokens > 0 
        ? Color.FromArgb(76, 175, 80)  // Green
        : Color.FromArgb(255, 193, 7); // Yellow
    
    // If there are promo tokens, show them
    if (service.PromoTokens > 0)
    {
        // Option 1: Show in tooltip
        toolTip1.SetToolTip(_lblTokens, 
            $"Regular: {service.RegularTokens}\nPromo: {service.PromoTokens} (used first)");
        
        // Option 2: Add promo indicator label
        if (_lblPromoTokens != null)
        {
            _lblPromoTokens.Text = $"+{service.PromoTokens} promo";
            _lblPromoTokens.Visible = true;
        }
    }
    else
    {
        if (_lblPromoTokens != null)
        {
            _lblPromoTokens.Visible = false;
        }
    }
}

// ============ 10. FORM CLOSING ============

private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
{
    // Log app close
    if (!string.IsNullOrEmpty(ProActivityLogger.Instance.AuthToken))
    {
        ProActivityLogger.Instance.LogAppClose();
    }
    
    // Unsubscribe from events
    TokenBalanceService.Instance.BalanceChanged -= OnTokenBalanceChanged;
}

// ============ 11. PERIODIC BALANCE REFRESH (Optional) ============

private System.Windows.Forms.Timer? _balanceRefreshTimer;

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
