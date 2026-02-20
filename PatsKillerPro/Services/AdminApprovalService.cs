using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Admin gating plumbing for high-risk tools (Engineering Mode, Target Blocks).
    ///
    /// - Fast path: allow-list / role claim.
    /// - Slow path: request approval via external endpoint (Loveable / Supabase function).
    ///
    /// Endpoint is configurable via PATSKILLER_ADMIN_APPROVAL_API.
    /// If not set, requests are stubbed (UI will prompt to configure).
    /// </summary>
    public sealed class AdminApprovalService
    {
        private static readonly Lazy<AdminApprovalService> _lazy = new(() => new AdminApprovalService());
        public static AdminApprovalService Instance => _lazy.Value;

        private static readonly string ApprovalApi =
            Environment.GetEnvironmentVariable("PATSKILLER_ADMIN_APPROVAL_API") ?? "";

        // Optional allowlist shortcut: comma-separated emails
        private static readonly HashSet<string> AdminEmails = new(
            (Environment.GetEnvironmentVariable("PATSKILLER_ADMIN_EMAILS") ?? "")
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);

        // Cache approvals to disk so a user doesn't need to re-request every restart
        private static readonly string CachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PatsKillerPro",
            "admin_approvals.json");

        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private readonly object _lock = new();
        private Dictionary<string, ApprovalCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

        private AdminApprovalService()
        {
            LoadCache();
        }

        public bool IsAdminByEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            return AdminEmails.Contains(email.Trim());
        }

        public bool IsAdminByRoleClaim(string? authToken)
        {
            if (string.IsNullOrWhiteSpace(authToken)) return false;
            try
            {
                var claims = JwtClaims.TryReadPayload(authToken);
                if (claims == null) return false;

                // Common patterns: "role": "admin" OR "roles": ["admin", ...]
                if (claims.TryGetValue("role", out var roleObj) && roleObj is string roleStr)
                    return roleStr.Contains("admin", StringComparison.OrdinalIgnoreCase);

                if (claims.TryGetValue("roles", out var rolesObj) && rolesObj is JsonElement el && el.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in el.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString()?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true)
                            return true;
                    }
                }

                return false;
            }
            catch { return false; }
        }

        public bool HasValidApproval(string requestType, string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(requestType)) return false;
            var key = MakeKey(requestType, userEmail);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    return entry.ExpiresAtUtc > DateTime.UtcNow;
                }
            }
            return false;
        }

        public bool IsEndpointConfigured => !string.IsNullOrWhiteSpace(ApprovalApi);

        public async Task<(bool Success, bool Approved, string Message, DateTime? ExpiresAtUtc)> RequestApprovalAsync(
            string requestType,
            string? userEmail,
            string? userId,
            string? authToken)
        {
            if (string.IsNullOrWhiteSpace(requestType))
                return (false, false, "Invalid request type", null);

            if (!IsEndpointConfigured)
                return (false, false, "Admin approval endpoint not configured. Set PATSKILLER_ADMIN_APPROVAL_API.", null);

            try
            {
                var payload = new
                {
                    requestType,
                    userId,
                    email = userEmail,
                    machineId = MachineIdentity.CombinedId,
                    requestedAtUtc = DateTime.UtcNow
                };

                var req = new HttpRequestMessage(HttpMethod.Post, ApprovalApi)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(authToken))
                    req.Headers.Add("Authorization", $"Bearer {authToken}");

                var resp = await _http.SendAsync(req);
                var json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    return (false, false, $"Approval request failed (HTTP {(int)resp.StatusCode}).", null);
                }

                // Expected: { approved: true/false, expiresAt: "..." , message: "..." }
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
                var root = doc.RootElement;
                var approved = root.TryGetProperty("approved", out var a) && a.ValueKind == JsonValueKind.True;

                DateTime? expiresAt = null;
                if (root.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(exp.GetString(), out var dt))
                        expiresAt = dt.ToUniversalTime();
                }

                var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString() ?? ""
                    : (approved ? "Approved" : "Pending approval");

                if (approved)
                {
                    // Default expiry: 24h if not provided
                    var effectiveExpiry = expiresAt ?? DateTime.UtcNow.AddHours(24);
                    SaveApproval(requestType, userEmail, effectiveExpiry);
                    return (true, true, message, effectiveExpiry);
                }

                return (true, false, message, expiresAt);
            }
            catch (Exception ex)
            {
                return (false, false, ex.Message, null);
            }
        }

        private void SaveApproval(string requestType, string? userEmail, DateTime expiresAtUtc)
        {
            var key = MakeKey(requestType, userEmail);
            lock (_lock)
            {
                _cache[key] = new ApprovalCacheEntry { ExpiresAtUtc = expiresAtUtc };
                PersistCache();
            }
        }

        private static string MakeKey(string requestType, string? userEmail)
            => $"{requestType}|{(userEmail ?? "").Trim().ToLowerInvariant()}";

        private void LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return;
                var json = File.ReadAllText(CachePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, ApprovalCacheEntry>>(json);
                if (dict != null)
                {
                    lock (_lock)
                        _cache = new Dictionary<string, ApprovalCacheEntry>(dict, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { /* ignore */ }
        }

        private void PersistCache()
        {
            try
            {
                var dir = Path.GetDirectoryName(CachePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Trim expired entries
                var now = DateTime.UtcNow;
                var cleaned = new Dictionary<string, ApprovalCacheEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in _cache)
                {
                    if (kv.Value.ExpiresAtUtc > now)
                        cleaned[kv.Key] = kv.Value;
                }
                _cache = cleaned;

                File.WriteAllText(CachePath, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* ignore */ }
        }

        private sealed class ApprovalCacheEntry
        {
            public DateTime ExpiresAtUtc { get; set; }
        }
    }
}
