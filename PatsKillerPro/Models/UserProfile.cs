using System;

namespace PatsKillerPro.Models
{
    /// <summary>
    /// User profile information
    /// </summary>
    public class UserProfile
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int PurchaseTokens { get; set; }
        public int PromoTokens { get; set; }
        public DateTime? PromoExpiry { get; set; }
        
        public int TotalTokens => PurchaseTokens + PromoTokens;
        
        public bool HasPromoTokens => PromoTokens > 0 && PromoExpiry > DateTime.UtcNow;
        
        public string PromoExpiryDisplay => PromoExpiry?.ToString("MMM d, yyyy") ?? "";
    }
}
