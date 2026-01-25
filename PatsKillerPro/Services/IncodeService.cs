using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Service for calculating incodes via the multi-provider routing system.
    /// Calls the provider-route-request edge function which handles:
    /// - Provider selection (priority, health, rotation status)
    /// - Automatic failover to backup providers
    /// - Rate limiting
    /// - Usage logging
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
        /// Calculate incode from outcode using the multi-provider routing system.
        /// The backend selects the best provider based on priority, health, and availability.
        /// </summary>
        /// <param name="outcode">The BCM/module outcode</param>
        /// <param name="vin">Vehicle VIN (optional, for logging)</param>
        /// <param name="moduleType">Module type: "BCM", "PCM", "ABS", "IPC" (optional)</param>
        /// <returns>IncodeResult with success status and incode or error</returns>
        public async Task<IncodeResult> CalculateIncodeAsync(string outcode, string? vin = null, string? moduleType = null)
        {
            try
            {
                var payload = new
                {
                    outcode = outcode,
                    vin = vin,
                    module_type = moduleType
                };

                var request = new ProviderRouteRequest
                {
                    ServiceType = "incode_calculation",
                    ActionType = "get_incode",
                    Payload = payload,
                    UserId = UserId,
                    UserEmail = UserEmail
                };

                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/provider-route-request")
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
                        Error = $"Provider API error: {response.StatusCode} - {responseBody}"
                    };
                }

                var result = JsonSerializer.Deserialize<ProviderRouteResponse>(responseBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result == null)
                {
                    return new IncodeResult { Success = false, Error = "Invalid response from provider" };
                }

                if (!result.Success)
                {
                    return new IncodeResult
                    {
                        Success = false,
                        Error = result.Error ?? "Provider returned failure",
                        ProviderUsed = result.ProviderUsed
                    };
                }

                // Extract incode from response data
                var incode = ExtractIncodeFromResponse(result.Data);

                return new IncodeResult
                {
                    Success = true,
                    Incode = incode,
                    ProviderUsed = result.ProviderUsed,
                    ResponseTimeMs = result.ResponseTimeMs
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
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/provider-health-check");
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

        private string? ExtractIncodeFromResponse(JsonElement? data)
        {
            if (data == null) return null;

            try
            {
                // Try common response formats
                if (data.Value.TryGetProperty("incode", out var incodeProp))
                {
                    return incodeProp.GetString();
                }
                if (data.Value.TryGetProperty("Incode", out var incodeProp2))
                {
                    return incodeProp2.GetString();
                }
                if (data.Value.TryGetProperty("result", out var resultProp))
                {
                    if (resultProp.TryGetProperty("incode", out var nestedIncode))
                    {
                        return nestedIncode.GetString();
                    }
                    return resultProp.GetString();
                }
                if (data.Value.TryGetProperty("code", out var codeProp))
                {
                    return codeProp.GetString();
                }

                // Return raw string if it looks like an incode
                var rawString = data.Value.ToString();
                if (!string.IsNullOrEmpty(rawString) && rawString.Length >= 4 && rawString.Length <= 20)
                {
                    return rawString;
                }
            }
            catch
            {
                // Ignore extraction errors
            }

            return null;
        }
    }

    // ============ REQUEST/RESPONSE MODELS ============

    public class ProviderRouteRequest
    {
        [JsonPropertyName("service_type")]
        public string ServiceType { get; set; } = "";

        [JsonPropertyName("action_type")]
        public string ActionType { get; set; } = "";

        [JsonPropertyName("payload")]
        public object? Payload { get; set; }

        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }

        [JsonPropertyName("user_email")]
        public string? UserEmail { get; set; }
    }

    public class ProviderRouteResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("data")]
        public JsonElement? Data { get; set; }

        [JsonPropertyName("provider_used")]
        public string? ProviderUsed { get; set; }

        [JsonPropertyName("response_time_ms")]
        public int ResponseTimeMs { get; set; }
    }

    public class IncodeResult
    {
        public bool Success { get; set; }
        public string? Incode { get; set; }
        public string? Error { get; set; }
        public string? ProviderUsed { get; set; }
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
