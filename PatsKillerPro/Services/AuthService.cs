using System;
using PatsKillerPro.Models;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Authentication service for patskiller.com integration
    /// Phase 1 implementation will add WebView2 login and token management
    /// </summary>
    public class AuthService
    {
        private UserProfile? _currentUser;
        private string? _accessToken;
        
        public event EventHandler? AuthStateChanged;
        public event EventHandler<UserProfile>? UserUpdated;
        
        public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);
        public UserProfile? CurrentUser => _currentUser;
        public string? AccessToken => _accessToken;
        
        public AuthService()
        {
            // Initialize with mock data for v3 development
            // Phase 1 will implement real authentication
            _currentUser = new UserProfile
            {
                Id = "mock-user-id",
                Email = "john@bestratemotors.com",
                Name = "John",
                PurchaseTokens = 47,
                PromoTokens = 12,
                PromoExpiry = new DateTime(2026, 3, 31)
            };
            _accessToken = "mock-token";
        }
        
        /// <summary>
        /// Process authentication response from WebView (Phase 1)
        /// </summary>
        public void ProcessAuthResponse(AuthResponse response)
        {
            if (!response.IsSuccess || response.Data == null)
            {
                throw new Exception(response.Error ?? "Authentication failed");
            }
            
            var data = response.Data;
            _accessToken = data.AccessToken;
            
            if (data.User != null)
            {
                _currentUser = new UserProfile
                {
                    Id = data.User.Id,
                    Email = data.User.Email,
                    Name = data.User.Name,
                    PurchaseTokens = data.User.TokenBalance,
                    PromoTokens = data.User.PromoTokens,
                    PromoExpiry = data.User.PromoExpiry
                };
            }
            
            AuthStateChanged?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Update token balance from API response
        /// </summary>
        public void UpdateTokenBalance(int purchaseTokens, int promoTokens)
        {
            if (_currentUser != null)
            {
                _currentUser.PurchaseTokens = purchaseTokens;
                _currentUser.PromoTokens = promoTokens;
                UserUpdated?.Invoke(this, _currentUser);
            }
        }
        
        /// <summary>
        /// Logout and clear credentials
        /// </summary>
        public void Logout()
        {
            _accessToken = null;
            _currentUser = null;
            AuthStateChanged?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Get unique machine identifier
        /// </summary>
        public string GetMachineId()
        {
            return MachineIdService.GetMachineId();
        }
    }
}
