using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Manages token balance with promo support and key session tracking.
    /// Promo tokens used FIRST before regular tokens.
    /// 
    /// TOKEN COSTS:
    /// - Key Session: 1 token (unlimited keys while same outcode)
    /// - Parameter Reset: 1 token per module (PCM, BCM, IPC, TCM = 3-4 total)
    /// - Utility Operations: 1 token each
    /// - Gateway Unlock: 1 token
    /// - Diagnostics: FREE
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

        public string? AuthToken { get; private set; }
        public string? UserEmail { get; private set; }
        public string? UserId { get; private set; }

        public int RegularTokens { get; private set; } = 0;
        public int PromoTokens { get; private set; } = 0;
        public int TotalTokens => RegularTokens + PromoTokens;
        public DateTime LastUpdated { get; private set; } = DateTime.MinValue;

        // Key programming session tracking
        private string? _keySessionOutcode;
        private string? _keySessionVin;
        private bool _keySessionActive;

        public event EventHandler<TokenBalanceChangedEventArgs>? BalanceChanged;

        private TokenBalanceService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public void SetAuthContext(string authToken, string userEmail, string? userId = null)
        {
            AuthToken = authToken;
            UserEmail = userEmail;
            UserId = userId;
        }

        public void ClearAuthContext()
        {
            AuthToken = null;
            UserEmail = null;
            UserId = null;
            RegularTokens = 0;
            PromoTokens = 0;
            LastUpdated = DateTime.MinValue;
            EndKeySession();
        }

        // ============ KEY SESSION MANAGEMENT ============

        /// <summary>
        /// Check if key session is active for this VIN/outcode (no charge needed)
        /// </summary>
        public bool IsKeySessionActive(string vin, string outcode)
        {
            return _keySessionActive && _keySessionVin == vin && _keySessionOutcode == outcode;
        }

        /// <summary>
        /// Start key programming session (1 token). Returns success=true with no charge if already active.
        /// </summary>
        public async Task<TokenDeductResult> StartKeySessionAsync(string vin, string outcode)
        {
            // Already active for same VIN/outcode - no charge
            if (IsKeySessionActive(vin, outcode))
            {
                Logger.Info("[TokenBalanceService] Key session already active, no charge");
                return new TokenDeductResult
                {
                    Success = true,
                    TotalDeducted = 0,
                    NewBalance = TotalTokens,
                    SessionAlreadyActive = true
                };
            }

            // End any existing session
            EndKeySession();

            // Charge 1 token
            var result = await DeductTokensAsync(1, "key_programming_session", vin);
            
            if (result.Success)
            {
                _keySessionActive = true;
                _keySessionVin = vin;
                _keySessionOutcode = outcode;
                Logger.Info($"[TokenBalanceService] Key session started for {vin}");
            }

            return result;
        }

        /// <summary>
        /// End key programming session (call when outcode changes or disconnect)
        /// </summary>
        public void EndKeySession()
        {
            if (_keySessionActive)
            {
                Logger.Info($"[TokenBalanceService] Key session ended for {_keySessionVin}");
            }
            _keySessionActive = false;
            _keySessionVin = null;
            _keySessionOutcode = null;
        }

        /// <summary>
        /// Check if key operation is free (within active session)
        /// </summary>
        public bool IsKeyOperationFree(string vin, string outcode)
        {
            return IsKeySessionActive(vin, outcode);
        }

        // ============ BALANCE METHODS ============

        public async Task<TokenBalanceResult> RefreshBalanceAsync()
        {
            if (string.IsNullOrEmpty(AuthToken))
            {
                return new TokenBalanceResult { Success = false, Error = "Not authenticated" };
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{SUPABASE_URL}/functions/v1/get-user-token-balance");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                request.Headers.Add("Authorization", $"Bearer {AuthToken}");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return await RefreshFromFallbackAsync();
                }

                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var regularTokens = 0;
                if (root.TryGetProperty("regularTokens", out var rt)) regularTokens = rt.GetInt32();
                else if (root.TryGetProperty("tokenBalance", out var tb)) regularTokens = tb.GetInt32();
                else if (root.TryGetProperty("tokens", out var t)) regularTokens = t.GetInt32();

                var promoTokens = 0;
                if (root.TryGetProperty("promoTokens", out var pt)) promoTokens = pt.GetInt32();
                else if (root.TryGetProperty("promo_tokens", out var pt2)) promoTokens = pt2.GetInt32();

                var oldTotal = TotalTokens;
                RegularTokens = regularTokens;
                PromoTokens = promoTokens;
                LastUpdated = DateTime.Now;

                Logger.Info($"[TokenBalanceService] Balance: Regular={RegularTokens}, Promo={PromoTokens}, Total={TotalTokens}");

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
                return await RefreshFromFallbackAsync();
            }
        }

        private async Task<TokenBalanceResult> RefreshFromFallbackAsync()
        {
            try
            {
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
                    if (root.TryGetProperty("tokenBalance", out var tb)) totalTokens = tb.GetInt32();
                    else if (root.TryGetProperty("tokens", out var t)) totalTokens = t.GetInt32();

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
                        TotalTokens = TotalTokens
                    };
                }

                return new TokenBalanceResult { Success = false, Error = $"HTTP {response.StatusCode}" };
            }
            catch (Exception ex)
            {
                return new TokenBalanceResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Deduct tokens (promo used first)
        /// </summary>
        public async Task<TokenDeductResult> DeductTokensAsync(int amount, string operationType, string? vin = null)
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
                var requestBody = new
                {
                    amount = amount,
                    operation_type = operationType,
                    vin = vin,
                    machine_id = ProActivityLogger.Instance.MachineId,
                    use_promo_first = true
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/deduct-user-tokens");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                request.Headers.Add("Authorization", $"Bearer {AuthToken}");
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorDoc = JsonDocument.Parse(json);
                    var errorMsg = errorDoc.RootElement.TryGetProperty("error", out var err) 
                        ? err.GetString() : $"HTTP {response.StatusCode}";
                    return new TokenDeductResult { Success = false, Error = errorMsg };
                }

                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var promoUsed = root.TryGetProperty("promo_used", out var pu) ? pu.GetInt32() : 0;
                var regularUsed = root.TryGetProperty("regular_used", out var ru) ? ru.GetInt32() : 0;
                var newRegular = root.TryGetProperty("regular_balance", out var rb) ? rb.GetInt32() : RegularTokens - regularUsed;
                var newPromo = root.TryGetProperty("promo_balance", out var pb) ? pb.GetInt32() : PromoTokens - promoUsed;
                
                var oldTotal = TotalTokens;
                RegularTokens = newRegular;
                PromoTokens = newPromo;
                LastUpdated = DateTime.Now;

                Logger.Info($"[TokenBalanceService] Deducted: Promo={promoUsed}, Regular={regularUsed}. Balance: {TotalTokens}");

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
        /// Deduct for parameter reset module (1 token)
        /// </summary>
        public async Task<TokenDeductResult> DeductForParamResetAsync(string moduleName, string vin)
        {
            return await DeductTokensAsync(1, $"param_reset_{moduleName.ToLowerInvariant()}", vin);
        }

        /// <summary>
        /// Deduct for utility operation (1 token)
        /// </summary>
        public async Task<TokenDeductResult> DeductForUtilityAsync(string operation, string vin)
        {
            return await DeductTokensAsync(1, $"utility_{operation.ToLowerInvariant()}", vin);
        }

        public bool HasEnoughTokens(int required) => TotalTokens >= required;

        public async Task RefreshAfterOperationAsync(int delayMs = 500)
        {
            await Task.Delay(delayMs);
            await RefreshBalanceAsync();
        }

        public void RefreshAfterOperation(int delayMs = 500)
        {
            _ = Task.Run(() => RefreshAfterOperationAsync(delayMs));
        }

        protected virtual void OnBalanceChanged(TokenBalanceChangedEventArgs e)
        {
            BalanceChanged?.Invoke(this, e);
        }

        public string GetDisplayString()
        {
            return PromoTokens > 0 ? $"{TotalTokens} ({RegularTokens} + {PromoTokens} promo)" : TotalTokens.ToString();
        }

        public string GetPromoIndicator()
        {
            return PromoTokens > 0 ? $"+{PromoTokens} promo" : "";
        }
    }

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
        public bool SessionAlreadyActive { get; set; }
    }

    public class TokenBalanceChangedEventArgs : EventArgs
    {
        public int RegularTokens { get; set; }
        public int PromoTokens { get; set; }
        public int TotalTokens { get; set; }
        public int PreviousTotal { get; set; }
    }
}
