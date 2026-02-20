using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Service for calculating incodes via the provider-router edge function.
    /// Handles:
    /// - Multi-provider routing (IncodeService, EZimmo)
    /// - 3-tier cache (user cache, global cache, provider call)
    /// - Token deduction and balance tracking
    /// - Automatic failover
    /// </summary>
    public class IncodeService
    {
        private static IncodeService? _instance;
        private static readonly object _lock = new object();

        public static IncodeService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new IncodeService();
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

        private IncodeService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
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
        }

        // ============ INCODE CALCULATION ============



        private static string MapProviderRouterError(string? errorCode, string? error, string? message, int? cooldownRemaining, int? cooldownMinutes)
        {
            var ec = (errorCode ?? "").Trim();
            var e = (error ?? "").Trim();
            var msg = (message ?? "").Trim();

            static string Lower(string s) => (s ?? string.Empty).ToLowerInvariant();

            if (ec.Equals("rate_limited", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(msg))
                {
                    if (cooldownRemaining.HasValue && cooldownMinutes.HasValue)
                        return $"{msg} Try again in {cooldownRemaining.Value} second(s).";
                    return msg;
                }
                return "Rate limit exceeded. Please wait before trying again.";
            }

            if (ec.Equals("insufficient_tokens", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(msg) ? msg : "Insufficient tokens to complete this conversion. Please purchase more tokens.";

            if (ec.Equals("conversion_in_progress", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(msg) ? msg : "This Out-Code is already being processed. Please wait for it to complete.";

            if (ec.Equals("INVALID_OUTCODE", StringComparison.OrdinalIgnoreCase) || ec.Equals("invalid_outcode", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(msg) ? msg : "The Out-Code entered is invalid. Please verify and try again.";

            if (ec.Equals("OUTCODE_ALREADY_USED", StringComparison.OrdinalIgnoreCase) || ec.Equals("outcode_already_used", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(msg) ? msg : "This Out-Code has already been used or is pending. Please generate a new Out-Code.";

            if ((Lower(e).Contains("routing") && Lower(e).Contains("not configured")) ||
                (Lower(msg).Contains("routing") && Lower(msg).Contains("not configured")))
                return "Service configuration error. Please contact support.";

            if (Lower(e).Contains("blocked") || Lower(msg).Contains("blocked") ||
                Lower(e).Contains("flagged") || Lower(msg).Contains("flagged"))
                return "This conversion request was flagged and cannot be processed. Please contact support.";

            if (Lower(e).Contains("timeout") || Lower(msg).Contains("timeout"))
                return "The request timed out. Please check your connection and try again.";

            if (!string.IsNullOrWhiteSpace(msg)) return msg;
            if (!string.IsNullOrWhiteSpace(e)) return e;
            return "Unable to calculate InCode. Please try again or contact support.";
        }

        /// <summary>
        /// Calculate incode from outcode using the provider-router edge function.
        /// API Format: { action: "calculate_incode", outcode, vehicleInfo }
        /// user_email is extracted from Bearer token server-side.
        /// </summary>
        public async Task<IncodeResult> CalculateIncodeAsync(string outcode, string? vin = null, string? moduleType = null, int? year = null, string? make = null, string? model = null)
        {
            
// Hard enforcement: BOTH SSO + valid license required for all token operations.
if (string.IsNullOrEmpty(AuthToken) || string.IsNullOrEmpty(UserEmail))
{
    return new IncodeResult { Success = false, Error = "SSO session required. Please sign in with Google." };
}

// LicenseService performs strict binding (machine + email). Treat missing license as a hard stop.
if (!LicenseService.Instance.IsLicensed)
{
    return new IncodeResult { Success = false, Error = "Active license required. Please activate your license key." };
}

try
            {
                // Build request in correct format for provider-router
                var request = new ProviderRouterRequest
                {
                    Action = "calculate_incode",
                    Outcode = outcode,
                    VehicleInfo = new VehicleInfoPayload
                    {
                        Vin = vin,
                        Year = year,
                        Make = make ?? "Ford",
                        Model = model,
                        ModuleType = moduleType ?? "BCM"
                    }
                };

                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/provider-router")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                httpRequest.Headers.Add("apikey", SUPABASE_ANON_KEY);
                if (!string.IsNullOrEmpty(AuthToken))
                {
                    httpRequest.Headers.Add("Authorization", $"Bearer {AuthToken}");
                }

                var response = await _httpClient.SendAsync(httpRequest);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    try
                    {
                        var errObj = JsonSerializer.Deserialize<ProviderRouterResponse>(responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        var friendly = MapProviderRouterError(errObj?.ErrorCode, errObj?.Error, errObj?.Message, errObj?.CooldownRemaining, errObj?.CooldownMinutes);
                        return new IncodeResult { Success = false, Error = friendly, ProviderUsed = errObj?.Source };
                    }
                    catch
                    {
                        return new IncodeResult { Success = false, Error = $"API error: {response.StatusCode}. Please try again or contact support." };
                    }
                }
var result = JsonSerializer.Deserialize<ProviderRouterResponse>(responseBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result == null)
                {
                    return new IncodeResult { Success = false, Error = "Invalid response" };
                }

                if (!result.Success)
                {
                    var friendly = MapProviderRouterError(result.ErrorCode, result.Error, result.Message, result.CooldownRemaining, result.CooldownMinutes);
                    return new IncodeResult
                    {
                        Success = false,
                        Error = friendly,
                        ProviderUsed = result.Source
                    };
                }
// Update token balance from response
                if (result.TokensRemaining.HasValue)
                {
                    TokenBalanceService.Instance.RefreshAfterOperation();
                }

                return new IncodeResult
                {
                    Success = true,
                    Incode = result.Incode,
                    ProviderUsed = result.Source,
                    TokensCharged = result.TokensCharged ?? 0,
                    TokensRemaining = result.TokensRemaining ?? 0,
                    IsOwnDuplicate = result.IsOwnDuplicate ?? false,
                    IsGlobalCache = result.IsGlobalCache ?? false,
                    ResponseTimeMs = result.ResponseTimeMs ?? 0
                };
            }
            catch (TaskCanceledException)
            {
                return new IncodeResult { Success = false, Error = "The request timed out. Please check your connection and try again." };
            }
            catch (HttpRequestException ex)
            {
                return new IncodeResult { Success = false, Error = $"Network error. Please check your connection and try again. ({ex.Message})" };
            }
            catch (Exception ex)
            {
                return new IncodeResult { Success = false, Error = $"Error: {ex.Message}" };
            }
        }

        /// <summary>
        /// Calculate incode for parameter reset (specific module).
        /// </summary>
        public async Task<IncodeResult> CalculateParamResetIncodeAsync(string outcode, string moduleName, string? vin = null)
        {
            return await CalculateIncodeAsync(outcode, vin, moduleName);
        }

        /// <summary>
        /// Check provider health status.
        /// </summary>
        public async Task<ProviderHealthResult> CheckProviderHealthAsync()
        {
            try
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{SUPABASE_URL}/functions/v1/provider-router?action=health");
                httpRequest.Headers.Add("apikey", SUPABASE_ANON_KEY);
                if (!string.IsNullOrEmpty(AuthToken))
                {
                    httpRequest.Headers.Add("Authorization", $"Bearer {AuthToken}");
                }

                var response = await _httpClient.SendAsync(httpRequest);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new ProviderHealthResult { Success = false, Error = responseBody };
                }

                var result = JsonSerializer.Deserialize<ProviderHealthResponse>(responseBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return new ProviderHealthResult
                {
                    Success = true,
                    Providers = result?.Providers
                };
            }
            catch (Exception ex)
            {
                return new ProviderHealthResult { Success = false, Error = ex.Message };
            }
        }
    }

    // ============ REQUEST/RESPONSE MODELS ============

    /// <summary>
    /// Request format for provider-router edge function
    /// </summary>
    public class ProviderRouterRequest
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = "calculate_incode";

        [JsonPropertyName("outcode")]
        public string Outcode { get; set; } = "";

        [JsonPropertyName("vehicleInfo")]
        public VehicleInfoPayload? VehicleInfo { get; set; }
    }

    public class VehicleInfoPayload
    {
        [JsonPropertyName("vin")]
        public string? Vin { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("make")]
        public string? Make { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("moduleType")]
        public string? ModuleType { get; set; }
    }

    /// <summary>
    /// Response format from provider-router edge function
    /// </summary>
    public class ProviderRouterResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("incode")]
        public string? Incode { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }


        [JsonPropertyName("error_code")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("cooldown_remaining")]
        public int? CooldownRemaining { get; set; }

        [JsonPropertyName("cooldown_minutes")]
        public int? CooldownMinutes { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("tokensCharged")]
        public int? TokensCharged { get; set; }

        [JsonPropertyName("tokensRemaining")]
        public int? TokensRemaining { get; set; }

        [JsonPropertyName("isOwnDuplicate")]
        public bool? IsOwnDuplicate { get; set; }

        [JsonPropertyName("isGlobalCache")]
        public bool? IsGlobalCache { get; set; }

        [JsonPropertyName("responseTimeMs")]
        public int? ResponseTimeMs { get; set; }
    }

    public class IncodeResult
    {
        public bool Success { get; set; }
        public string? Incode { get; set; }
        public string? Error { get; set; }
        public string? ProviderUsed { get; set; }
        public int TokensCharged { get; set; }
        public int TokensRemaining { get; set; }
        public bool IsOwnDuplicate { get; set; }
        public bool IsGlobalCache { get; set; }
        public int ResponseTimeMs { get; set; }
    }

    public class ProviderHealthResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public ProviderInfo[]? Providers { get; set; }
    }

    public class ProviderHealthResponse
    {
        [JsonPropertyName("providers")]
        public ProviderInfo[]? Providers { get; set; }
    }

    public class ProviderInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("is_healthy")]
        public bool IsHealthy { get; set; }

        [JsonPropertyName("is_in_rotation")]
        public bool IsInRotation { get; set; }

        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        [JsonPropertyName("last_health_check")]
        public DateTime? LastHealthCheck { get; set; }
    }
}
