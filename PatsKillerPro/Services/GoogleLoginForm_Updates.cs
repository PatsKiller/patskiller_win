// ============================================================
// GoogleLoginForm.cs - INTEGRATION UPDATES
// Add these changes to your existing GoogleLoginForm.cs
// ============================================================

// 1. ADD THESE USING STATEMENTS at the top of the file:
using PatsKillerPro.Services;

// 2. ADD THESE NEW PROPERTIES to the class (after existing properties):

// New: Promo token properties
public int RegularTokenCount { get; private set; }
public int PromoTokenCount { get; private set; }

// 3. UPDATE THE RESULT PROPERTY to use total:
// public int TokenCount => RegularTokenCount + PromoTokenCount;

// ============================================================
// 4. REPLACE the FetchTokenCountAsync method with this version:
// ============================================================

/// <summary>
/// FIX: Fetch actual token balance from API after authentication (including promo tokens)
/// </summary>
private async Task<int> FetchTokenCountAsync(string accessToken)
{
    try
    {
        // Try the new endpoint that includes promo tokens
        var request = new HttpRequestMessage(HttpMethod.Get, $"{SUPABASE_URL}/functions/v1/get-user-token-balance");
        request.Headers.Add("apikey", SUPABASE_ANON_KEY);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        Logger.Debug($"FetchTokenCount (new endpoint) response: {response.StatusCode} - {json}");

        if (response.IsSuccessStatusCode)
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            // Parse regular tokens
            if (root.TryGetProperty("regularTokens", out var rt))
                RegularTokenCount = rt.GetInt32();
            else if (root.TryGetProperty("tokenBalance", out var tb))
                RegularTokenCount = tb.GetInt32();
            else if (root.TryGetProperty("tokens", out var t))
                RegularTokenCount = t.GetInt32();
            
            // Parse promo tokens
            if (root.TryGetProperty("promoTokens", out var pt))
                PromoTokenCount = pt.GetInt32();
            else if (root.TryGetProperty("promo_tokens", out var pt2))
                PromoTokenCount = pt2.GetInt32();
            else
                PromoTokenCount = 0;
            
            return RegularTokenCount + PromoTokenCount;
        }
        
        // Fallback to get-user-tokens endpoint
        return await FetchTokenCountFromFallbackAsync(accessToken);
    }
    catch (Exception ex)
    {
        Logger.Error("FetchTokenCountAsync error", ex);
        return await FetchTokenCountFromFallbackAsync(accessToken);
    }
}

private async Task<int> FetchTokenCountFromFallbackAsync(string accessToken)
{
    try
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{SUPABASE_URL}/functions/v1/get-user-tokens");
        request.Headers.Add("apikey", SUPABASE_ANON_KEY);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        Logger.Debug($"FetchTokenCount (fallback) response: {response.StatusCode} - {json}");

        if (response.IsSuccessStatusCode)
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("tokens", out var tokens)) 
            {
                RegularTokenCount = tokens.GetInt32();
                PromoTokenCount = 0;
                return RegularTokenCount;
            }
            if (root.TryGetProperty("tokenBalance", out var balance))
            {
                RegularTokenCount = balance.GetInt32();
                PromoTokenCount = 0;
                return RegularTokenCount;
            }
        }
        
        return 0;
    }
    catch (Exception ex)
    {
        Logger.Error("FetchTokenCountFromFallbackAsync error", ex);
        return 0;
    }
}

// ============================================================
// 5. UPDATE ShowSuccessState to show promo tokens:
// ============================================================

private void ShowSuccessState(string email, int tokens)
{
    foreach (Control c in _successPanel.Controls)
    {
        if (c.Name == "lblSuccessEmail")
        {
            c.Text = email;
            c.Location = new Point((_successPanel.Width - c.PreferredSize.Width) / 2, c.Location.Y);
        }
    }
    foreach (Control c in _successPanel.Controls)
    {
        if (c is Panel tokenBox)
        {
            foreach (Control tc in tokenBox.Controls)
            {
                if (tc.Name == "lblSuccessTokens")
                {
                    // Show total tokens
                    tc.Text = tokens.ToString();
                    tc.Location = new Point(tokenBox.Width - tc.PreferredSize.Width - 25, 15);
                }
            }
        }
    }

    _loginPanel.Visible = false;
    _waitingPanel.Visible = false;
    _successPanel.Visible = true;
    _errorPanel.Visible = false;
    CenterPanel(_successPanel);

    // Update header with promo indicator
    if (PromoTokenCount > 0)
    {
        _lblTokens.Text = $"Tokens: {tokens}";
        // Optional: Add tooltip or secondary text showing promo breakdown
    }
    else
    {
        _lblTokens.Text = $"Tokens: {tokens}";
    }
    _lblStatus.Text = email;
    PositionHeaderLabels();
    
    // Initialize the services with auth context
    InitializeServicesAfterLogin();
    
    AutoCloseAfterSuccess();
}

// ============================================================
// 6. ADD this new method to initialize services after login:
// ============================================================

private void InitializeServicesAfterLogin()
{
    try
    {
        // Set up ProActivityLogger
        ProActivityLogger.Instance.SetAuthContext(AuthToken!, UserEmail!, null);
        
        // Set up TokenBalanceService
        TokenBalanceService.Instance.SetAuthContext(AuthToken!, UserEmail!, null);
        
        // Log the successful login
        ProActivityLogger.Instance.LogLogin(UserEmail!, success: true);
        
        // Log app start if this is first login
        ProActivityLogger.Instance.LogAppStart();
        
        Logger.Info("[GoogleLoginForm] Services initialized after login");
    }
    catch (Exception ex)
    {
        Logger.Warning($"[GoogleLoginForm] Failed to initialize services: {ex.Message}");
    }
}

// ============================================================
// 7. ADD cleanup in AutoCloseAfterSuccess or add a new cleanup method:
// ============================================================

// Call this when user logs out (add to your logout handler):
private void CleanupOnLogout()
{
    try
    {
        // Log the logout
        ProActivityLogger.Instance.LogLogout(UserEmail ?? "unknown");
        
        // Clear service contexts
        ProActivityLogger.Instance.ClearAuthContext();
        TokenBalanceService.Instance.ClearAuthContext();
    }
    catch { }
}

// ============================================================
// 8. UPDATE the CheckSessionAsync to parse promo tokens:
// ============================================================

// In CheckSessionAsync, after parsing tokenBalance, add:
//
// if (root.TryGetProperty("regularTokens", out var regularTk))
//     result.RegularTokens = regularTk.GetInt32();
// if (root.TryGetProperty("promoTokens", out var promoTk))
//     result.PromoTokens = promoTk.GetInt32();

// ============================================================
// 9. UPDATE SessionResult class to include promo tokens:
// ============================================================

private class SessionResult 
{ 
    public string Status { get; set; } = ""; 
    public string? Token { get; set; } 
    public string? RefreshToken { get; set; } 
    public string? Email { get; set; } 
    public int TokenCount { get; set; }
    public int RegularTokens { get; set; }  // ADD THIS
    public int PromoTokens { get; set; }     // ADD THIS
}
