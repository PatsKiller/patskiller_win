using System;
using System.Text.Json.Serialization;

namespace PatsKillerPro.Models
{
    /// <summary>
    /// Token balance information
    /// </summary>
    public class TokenBalance
    {
        public int PurchaseTokens { get; set; }
        public int PromoTokens { get; set; }
        public DateTime? PromoExpiry { get; set; }
        
        public string PurchaseDisplay => PurchaseTokens.ToString();
        public string PromoDisplay => PromoTokens > 0 ? $"{PromoTokens} promo" : "";
    }
    
    /// <summary>
    /// Authentication response from bridge-login page
    /// </summary>
    public class AuthResponse
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
        
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
        
        [JsonPropertyName("data")]
        public AuthData? Data { get; set; }
        
        [JsonPropertyName("error")]
        public string? Error { get; set; }
        
        public bool IsSuccess => Status == "success" && Data != null;
    }

    public class AuthData
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;
        
        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;
        
        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }
        
        [JsonPropertyName("user")]
        public UserData? User { get; set; }
    }

    public class UserData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonPropertyName("tokenBalance")]
        public int TokenBalance { get; set; }
        
        [JsonPropertyName("promoTokens")]
        public int PromoTokens { get; set; }
        
        [JsonPropertyName("promoExpiry")]
        public DateTime? PromoExpiry { get; set; }
    }

    /// <summary>
    /// Incode response from API
    /// </summary>
    public class IncodeResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
        
        [JsonPropertyName("incode")]
        public string? Incode { get; set; }
        
        [JsonPropertyName("tokensUsed")]
        public int TokensUsed { get; set; }
        
        [JsonPropertyName("tokenSource")]
        public string? TokenSource { get; set; }
        
        [JsonPropertyName("tokensRemaining")]
        public TokensRemaining? TokensRemaining { get; set; }
        
        [JsonPropertyName("transactionId")]
        public string? TransactionId { get; set; }
        
        [JsonPropertyName("error")]
        public string? Error { get; set; }
        
        [JsonPropertyName("message")]
        public string? Message { get; set; }
        
        public bool IsSuccess => Status == "success" && !string.IsNullOrEmpty(Incode);
    }

    public class TokensRemaining
    {
        [JsonPropertyName("purchase")]
        public int Purchase { get; set; }
        
        [JsonPropertyName("promo")]
        public int Promo { get; set; }
    }
}
