using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using PatsKillerPro.Models;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// API client for patskiller.com integration
    /// Phase 2 implementation will add automatic incode retrieval
    /// </summary>
    public class ApiClient
    {
        private const string BASE_URL = "https://patskiller.com/api/v2/bridge";
        
        private readonly HttpClient _httpClient;
        private readonly AuthService? _authService;
        
        public ApiClient(AuthService? authService = null)
        {
            _authService = authService;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(BASE_URL),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }
        
        /// <summary>
        /// Request incode for outcode (Phase 2)
        /// </summary>
        public async Task<IncodeResponse> GetIncodeAsync(
            string outcode,
            string vin,
            string module,
            string moduleAddress,
            string? sessionId = null)
        {
            // Phase 2 implementation
            // For now, return mock response
            await Task.Delay(100);
            
            return new IncodeResponse
            {
                Status = "success",
                Incode = "MOCK1234",
                TokensUsed = 1,
                TokenSource = "promo",
                TokensRemaining = new TokensRemaining
                {
                    Purchase = 47,
                    Promo = 11
                }
            };
        }
        
        /// <summary>
        /// Get current token balance (Phase 2)
        /// </summary>
        public async Task<TokenBalance> GetBalanceAsync()
        {
            // Phase 2 implementation
            await Task.Delay(100);
            
            return new TokenBalance
            {
                PurchaseTokens = 47,
                PromoTokens = 12,
                PromoExpiry = new DateTime(2026, 3, 31)
            };
        }
        
        /// <summary>
        /// Start gateway unlock session (Phase 2)
        /// </summary>
        public async Task<GatewaySessionResponse> StartGatewaySessionAsync(string vin)
        {
            // Phase 2 implementation
            await Task.Delay(100);
            
            return new GatewaySessionResponse
            {
                Status = "success",
                SessionId = "sess_mock123",
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                FreeOperations = new[] { "key_program", "key_erase" },
                TokensCharged = 1
            };
        }
        
        private void AddAuthHeaders(HttpRequestMessage request)
        {
            if (_authService != null && _authService.IsAuthenticated)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer", _authService.AccessToken);
                request.Headers.Add("X-Machine-ID", _authService.GetMachineId());
                request.Headers.Add("X-Client-Version", "2.0.0");
            }
        }
    }
    
    public class GatewaySessionResponse
    {
        public string Status { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string[]? FreeOperations { get; set; }
        public int TokensCharged { get; set; }
        public string? Error { get; set; }
    }
}
