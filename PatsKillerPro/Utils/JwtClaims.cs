using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace PatsKillerPro.Utils
{
    /// <summary>
    /// Minimal JWT payload reader. No validation, only parsing.
    /// Used for local feature gating (e.g., role claims).
    /// </summary>
    public static class JwtClaims
    {
        public static Dictionary<string, object?>? TryReadPayload(string jwt)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(jwt)) return null;
                var parts = jwt.Split('.');
                if (parts.Length < 2) return null;

                var payload = parts[1];
                var jsonBytes = Base64UrlDecode(payload);
                var json = Encoding.UTF8.GetString(jsonBytes);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in root.EnumerateObject())
                {
                    // Keep JsonElement for complex types to avoid losing structure
                    dict[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value,
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Object => prop.Value,
                        JsonValueKind.Array => prop.Value,
                        _ => null
                    };
                }
                return dict;
            }
            catch { return null; }
        }

        private static byte[] Base64UrlDecode(string input)
        {
            var s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }
    }
}
