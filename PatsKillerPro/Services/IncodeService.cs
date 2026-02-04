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

        /// <summary>
        /// Calculate incode from outcode using the provider-router edge function.
        /// API Format: { action: "calculate_incode", outcode, vehicleInfo }
        /// user_email is extracted from Bearer token server-side.
        /// </summary>
        public async Task<IncodeResult> CalculateIncodeAsync(string outcode, string? vin = null, string? moduleType = null, int? year = null, string? make = null, string? model = null)
        {
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
                    return new IncodeResult
                    {
                        Success = false,
                        Error = $"API error: {response.StatusCode} - {responseBody}"
                    };
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
                    return new IncodeResult
                    {
                        Success = false,
                        Error = result.Error ?? "Provider returned failure",
                        ProviderUsed = result.Source
                    };
                }

                // Update token balance from response
                if (result.TokensRemaining.HasValue)
                {
                    TokenBalanceService.Instance.UpdateFromServerResponse(result.TokensRemaining.Value);
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
                return new IncodeResult { Success = false, Error = "Request timed out" };
            }
            catch (HttpRequestException ex)
            {
                return new IncodeResult { Success = false, Error = $"Network error: {ex.Message}" };
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
