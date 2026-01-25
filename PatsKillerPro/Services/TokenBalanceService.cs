using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Service to manage token balance with promo token support
    /// Refreshes balance after each operation and displays combined totals
    /// Promo tokens are used FIRST before regular tokens
    /// </summary>
    public class TokenBalanceService
    {
        private static TokenBalanceService? _instance;
        private static readonly object _lock = new object();
        
        public static TokenBalanceService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new TokenBalanceService();
                    }
                }
                return _instance;
            }
        }

        private readonly HttpClient _httpClient;
        private const string SUPABASE_URL = "https://kmpnplpijuzzbftsjacx.supabase.co";
        private const string SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpanV6emJmdHNqYWN4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3MzA5ODgwMTgsImV4cCI6MjA0NjU2NDAxOH0.iqKMFa_Ye7LCG-n7F1a1rgdsVBPkz3TmT_x0lMm8TT8";

        // Auth context
        public string? AuthToken { get; private set; }
        public string? UserEmail { get; private set; }
        public string? UserId { get; private set; }

        // Current balance state
        public int RegularTokens { get; private set; } = 0;
        public int PromoTokens { get; private set; } = 0;
        public int TotalTokens => RegularTokens + PromoTokens;
        public DateTime LastUpdated { get; private set; } = DateTime.MinValue;

        // Events for UI updates
        public event EventHandler<TokenBalanceChangedEventArgs>? BalanceChanged;

        private TokenBalanceService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>
        /// Set auth context after login
        /// </summary>
        public void SetAuthContext(string authToken, string userEmail, string? userId = null)
        {
            AuthToken = authToken;
            UserEmail = userEmail;
            UserId = userId;
        }

        /// <summary>
        /// Clear auth context on logout
        /// </summary>
        public void ClearAuthContext()
        {
            AuthToken = null;
            UserEmail = null;
            UserId = null;
            RegularTokens = 0;
            PromoTokens = 0;
            LastUpdated = DateTime.MinValue;
        }

        /// <summary>
        /// Fetch current token balance from backend (including promo tokens)
        /// </summary>
        public async Task<TokenBalanceResult> RefreshBalanceAsync()
        {
            if (string.IsNullOrEmpty(AuthToken))
            {
                Logger.Warning("[TokenBalanceService] No auth token, cannot refresh balance");
                return new TokenBalanceResult { Success = false, Error = "Not authenticated" };
            }

            try
            {
                Logger.Info("[TokenBalanceService] Refreshing token balance...");

                // Try the new endpoint first
                var request = new HttpRequestMessage(HttpMethod.Get, $"{SUPABASE_URL}/functions/v1/get-user-token-balance");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                request.Headers.Add("Authorization", $"Bearer {AuthToken}");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                Logger.Debug($"[TokenBalanceService] Response: {response.StatusCode} - {json}");

                if (!response.IsSuccessStatusCode)
                {
                    // Fallback to older endpoint
                    return await RefreshBalanceFromFallbackAsync();
                }

                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Parse regular tokens
                var regularTokens = 0;
                if (root.TryGetProperty("regularTokens", out var rt))
                    regularTokens = rt.GetInt32();
                else if (root.TryGetProperty("tokens", out var t))
                    regularTokens = t.GetInt32();
                else if (root.TryGetProperty("tokenBalance", out var tb))
                    regularTokens = tb.GetInt32();

                // Parse promo tokens
                var promoTokens = 0;
                if (root.TryGetProperty("promoTokens", out var pt))
                    promoTokens = pt.GetInt32();
                else if (root.TryGetProperty("promo_tokens", out var pt2))
                    promoTokens = pt2.GetInt32();

                // Update state
                var oldTotal = TotalTokens;
                RegularTokens = regularTokens;
                PromoTokens = promoTokens;
                LastUpdated = DateTime.Now;

                Logger.Info($"[TokenBalanceService] Balance updated: Regular={RegularTokens}, Promo={PromoTokens}, Total={TotalTokens}");

                // Fire event if balance changed
                if (oldTotal != TotalTokens)
                {
                    OnBalanceChanged(new TokenBalanceChangedEventArgs
                    {
                        RegularTokens = RegularTokens,
                        PromoTokens = PromoTokens,
                        TotalTokens = TotalTokens,
                        PreviousTotal = oldTotal
                    });
                }

                return new TokenBalanceResult
                {
                    Success = true,
                    RegularTokens = RegularTokens,
                    PromoTokens = PromoTokens,
                    TotalTokens = TotalTokens
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"[TokenBalanceService] RefreshBalanceAsync error: {ex.Message}");
                return await RefreshBalanceFromFallbackAsync();
            }
        }

        /// <summary>
        /// Fallback to older endpoints
        /// </summary>
        private async Task<TokenBalanceResult> RefreshBalanceFromFallbackAsync()
        {
            try
            {
                Logger.Info("[TokenBalanceService] Trying fallback endpoints...");

                // Try get-user-tokens
                var request = new HttpRequestMessage(HttpMethod.Get, $"{SUPABASE_URL}/functions/v1/get-user-tokens");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                request.Headers.Add("Authorization", $"Bearer {AuthToken}");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var totalTokens = 0;
                    if (root.TryGetProperty("tokenBalance", out var tb))
                        totalTokens = tb.GetInt32();
                    else if (root.TryGetProperty("tokens", out var t))
                        totalTokens = t.GetInt32();

                    var oldTotal = TotalTokens;
                    RegularTokens = totalTokens;
                    PromoTokens = 0;
                    LastUpdated = DateTime.Now;

                    if (oldTotal != TotalTokens)
                    {
                        OnBalanceChanged(new TokenBalanceChangedEventArgs
                        {
                            RegularTokens = RegularTokens,
                            PromoTokens = PromoTokens,
                            TotalTokens = TotalTokens,
                            PreviousTotal = oldTotal
                        });
                    }

                    return new TokenBalanceResult
                    {
                        Success = true,
                        RegularTokens = RegularTokens,
                        PromoTokens = PromoTokens,
                        TotalTokens = TotalTokens
                    };
                }

                Logger.Warning($"[TokenBalanceService] Fallback failed: {response.StatusCode}");
                return new TokenBalanceResult { Success = false, Error = $"HTTP {response.StatusCode}" };
            }
            catch (Exception ex)
            {
                Logger.Error($"[TokenBalanceService] Fallback error: {ex.Message}");
                return new TokenBalanceResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Deduct tokens for an operation
        /// Promo tokens are used FIRST before regular tokens
        /// </summary>
        public async Task<TokenDeductResult> DeductTokensAsync(int amount, string operationType, 
            string? vin = null, string? vehicleModel = null)
        {
            if (string.IsNullOrEmpty(AuthToken))
            {
                return new TokenDeductResult { Success = false, Error = "Not authenticated" };
            }

            if (TotalTokens < amount)
            {
                return new TokenDeductResult 
                { 
                    Success = false, 
                    Error = "Insufficient tokens",
                    RequiredTokens = amount,
                    AvailableTokens = TotalTokens
                };
            }

            try
            {
                Logger.Info($"[TokenBalanceService] Deducting {amount} tokens for {operationType}...");

                var requestBody = new
                {
                    amount = amount,
                    operation_type = operationType,
                    vin = vin,
                    vehicle_model = vehicleModel,
                    machine_id = ProActivityLogger.Instance.MachineId,
                    use_promo_first = true // Always use promo tokens first
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/deduct-user-tokens");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                request.Headers.Add("Authorization", $"Bearer {AuthToken}");
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                Logger.Debug($"[TokenBalanceService] Deduct response: {response.StatusCode} - {json}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorDoc = JsonDocument.Parse(json);
                    var errorMsg = errorDoc.RootElement.TryGetProperty("error", out var err) 
                        ? err.GetString() 
                        : $"HTTP {response.StatusCode}";
                    
                    return new TokenDeductResult { Success = false, Error = errorMsg };
                }

                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Parse deduction breakdown
                var promoUsed = root.TryGetProperty("promo_used", out var pu) ? pu.GetInt32() : 0;
                var regularUsed = root.TryGetProperty("regular_used", out var ru) ? ru.GetInt32() : 0;
                var newBalance = root.TryGetProperty("new_balance", out var nb) ? nb.GetInt32() : TotalTokens - amount;

                // Update local state
                var newRegular = root.TryGetProperty("regular_balance", out var rb) ? rb.GetInt32() : RegularTokens - regularUsed;
                var newPromo = root.TryGetProperty("promo_balance", out var pb) ? pb.GetInt32() : PromoTokens - promoUsed;
                
                var oldTotal = TotalTokens;
                RegularTokens = newRegular;
                PromoTokens = newPromo;
                LastUpdated = DateTime.Now;

                Logger.Info($"[TokenBalanceService] Deducted: Promo={promoUsed}, Regular={regularUsed}. New balance: {TotalTokens}");

                OnBalanceChanged(new TokenBalanceChangedEventArgs
                {
                    RegularTokens = RegularTokens,
                    PromoTokens = PromoTokens,
                    TotalTokens = TotalTokens,
                    PreviousTotal = oldTotal
                });

                return new TokenDeductResult
                {
                    Success = true,
                    PromoTokensUsed = promoUsed,
                    RegularTokensUsed = regularUsed,
                    TotalDeducted = promoUsed + regularUsed,
                    NewBalance = TotalTokens
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"[TokenBalanceService] DeductTokensAsync error: {ex.Message}");
                return new TokenDeductResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Quick check if user has enough tokens
        /// </summary>
        public bool HasEnoughTokens(int required)
        {
            return TotalTokens >= required;
        }

        /// <summary>
        /// Refresh balance after an operation completes
        /// Call this after every token-consuming operation
        /// </summary>
        public async Task RefreshAfterOperationAsync(int delayMs = 500)
        {
            // Brief delay to let backend process
            await Task.Delay(delayMs);
            await RefreshBalanceAsync();
        }

        /// <summary>
        /// Fire and forget refresh (doesn't block UI)
        /// </summary>
        public void RefreshAfterOperation(int delayMs = 500)
        {
            _ = Task.Run(() => RefreshAfterOperationAsync(delayMs));
        }

        protected virtual void OnBalanceChanged(TokenBalanceChangedEventArgs e)
        {
            BalanceChanged?.Invoke(this, e);
        }

        /// <summary>
        /// Format balance for display (e.g., "5 (3 + 2 promo)")
        /// </summary>
        public string GetDisplayString()
        {
            if (PromoTokens > 0)
            {
                return $"{TotalTokens} ({RegularTokens} + {PromoTokens} promo)";
            }
            return TotalTokens.ToString();
        }

        /// <summary>
        /// Get formatted balance for header display
        /// </summary>
        public string GetHeaderDisplayString()
        {
            if (PromoTokens > 0)
            {
                return $"Tokens: {TotalTokens}";
            }
            return $"Tokens: {TotalTokens}";
        }

        /// <summary>
        /// Get promo indicator text (for secondary label)
        /// </summary>
        public string GetPromoIndicator()
        {
            if (PromoTokens > 0)
            {
                return $"+{PromoTokens} promo";
            }
            return "";
        }
    }

    // ============ RESULT CLASSES ============

    public class TokenBalanceResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int RegularTokens { get; set; }
        public int PromoTokens { get; set; }
        public int TotalTokens { get; set; }
    }

    public class TokenDeductResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int PromoTokensUsed { get; set; }
        public int RegularTokensUsed { get; set; }
        public int TotalDeducted { get; set; }
        public int NewBalance { get; set; }
        public int RequiredTokens { get; set; }
        public int AvailableTokens { get; set; }
    }

    public class TokenBalanceChangedEventArgs : EventArgs
    {
        public int RegularTokens { get; set; }
        public int PromoTokens { get; set; }
        public int TotalTokens { get; set; }
        public int PreviousTotal { get; set; }
    }
}
