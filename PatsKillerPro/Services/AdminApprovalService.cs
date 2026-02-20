using System;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Admin approval workflow (Loveable/Supabase).
    ///
    /// Read-only approval check:
    ///   GET /rest/v1/access_requests?email=eq.{USER_EMAIL}&select=status,reviewed_at,notes
    ///
    /// Request access (direct insert):
    ///   POST /rest/v1/access_requests { email, machine_id, requested_at, status:"pending" }
    ///
    /// Auth: uses the logged-in user's JWT in Authorization: Bearer {jwt}
    /// plus Supabase 'apikey' header.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class AdminApprovalService
    {
        private static AdminApprovalService? _instance;
        private static readonly object _lock = new();
        public static AdminApprovalService Instance
        {
            get
            {
                if (_instance == null)
                    lock (_lock) { _instance ??= new AdminApprovalService(); }
                return _instance;
            }
        }

        private static readonly string SUPABASE_URL =
            Environment.GetEnvironmentVariable("PATSKILLER_ADMIN_SUPABASE_URL")
            ?? Environment.GetEnvironmentVariable("PATSKILLER_SUPABASE_URL")
            ?? "https://ljltukcvxisvxkugftkr.supabase.co";

        private static readonly string SUPABASE_ANON_KEY =
            Environment.GetEnvironmentVariable("PATSKILLER_ADMIN_SUPABASE_ANON_KEY")
            ?? Environment.GetEnvironmentVariable("PATSKILLER_SUPABASE_ANON_KEY")
            ?? "";

        private static readonly string ACCESS_REQUESTS_API =
            Environment.GetEnvironmentVariable("PATSKILLER_ADMIN_ACCESS_REQUESTS_API")
            ?? $"{SUPABASE_URL}/rest/v1/access_requests";

        private readonly HttpClient _http = new();
        private string? _authToken;
        private string? _userEmail;

        // Light cache to avoid hammering Supabase on repeated clicks.
        private ApprovalRecord? _last;
        private DateTime _lastFetchedUtc;
        private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(20);

        private AdminApprovalService() { }

        public void SetAuthContext(string? authToken, string? userEmail)
        {
            _authToken = authToken;
            _userEmail = userEmail;
            _last = null;
            _lastFetchedUtc = DateTime.MinValue;
        }

        public bool HasAuth => !string.IsNullOrWhiteSpace(_authToken) && !string.IsNullOrWhiteSpace(_userEmail);

        public async Task<ApprovalRecord?> GetApprovalAsync(CancellationToken ct = default)
        {
            if (!HasAuth) return null;

            if (_last != null && (DateTime.UtcNow - _lastFetchedUtc) < _cacheTtl)
                return _last;

            var email = Uri.EscapeDataString(_userEmail!);
            var url = $"{ACCESS_REQUESTS_API}?email=eq.{email}&select=status,reviewed_at,notes&limit=1";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(req);

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log(Logger.LogLevel.Warning, $"[AdminApproval] GET failed ({(int)resp.StatusCode}): {TrySummarize(body)}");
                return null;
            }

            var record = TryParseFirstRecord(body);
            _last = record;
            _lastFetchedUtc = DateTime.UtcNow;
            return record;
        }

        public async Task<(bool Success, string Message)> RequestAccessAsync(CancellationToken ct = default)
        {
            if (!HasAuth) return (false, "Not authenticated");

            var payload = new
            {
                email = _userEmail,
                machine_id = MachineIdentity.MachineId,
                requested_at = DateTime.UtcNow.ToString("O"),
                status = "pending"
            };

            var json = JsonSerializer.Serialize(payload);

            using var req = new HttpRequestMessage(HttpMethod.Post, ACCESS_REQUESTS_API)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            ApplyHeaders(req);
            req.Headers.TryAddWithoutValidation("Prefer", "return=representation");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (resp.IsSuccessStatusCode)
            {
                _last = null;
                _lastFetchedUtc = DateTime.MinValue;
                return (true, "Access request submitted");
            }

            if (resp.StatusCode == HttpStatusCode.Conflict)
                return (false, "Request already exists (pending review)");

            return (false, $"Request failed ({(int)resp.StatusCode}): {TrySummarize(body)}");
        }

        private void ApplyHeaders(HttpRequestMessage req)
        {
            if (!string.IsNullOrWhiteSpace(SUPABASE_ANON_KEY))
                req.Headers.TryAddWithoutValidation("apikey", SUPABASE_ANON_KEY);

            if (!string.IsNullOrWhiteSpace(_authToken))
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_authToken}");

            req.Headers.TryAddWithoutValidation("Accept", "application/json");
        }

        private static ApprovalRecord? TryParseFirstRecord(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0) return null;
                    return ApprovalRecord.FromJson(root[0]);
                }
                if (root.ValueKind == JsonValueKind.Object)
                {
                    return ApprovalRecord.FromJson(root);
                }
            }
            catch { }

            return null;
        }

        private static string TrySummarize(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "(empty)";
            body = body.Trim();
            return body.Length <= 180 ? body : body.Substring(0, 180) + "…";
        }

        public sealed class ApprovalRecord
        {
            public string? Status { get; init; }
            public DateTime? ReviewedAtUtc { get; init; }
            public string? Notes { get; init; }

            public bool IsApproved => string.Equals(Status, "approved", StringComparison.OrdinalIgnoreCase);
            public bool IsPending => string.Equals(Status, "pending", StringComparison.OrdinalIgnoreCase);
            public bool IsRejected => string.Equals(Status, "rejected", StringComparison.OrdinalIgnoreCase);

            public static ApprovalRecord FromJson(JsonElement obj)
            {
                string? status = null;
                string? notes = null;
                DateTime? reviewedAt = null;

                if (obj.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String)
                    status = s.GetString();
                if (obj.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String)
                    notes = n.GetString();
                if (obj.TryGetProperty("reviewed_at", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(r.GetString(), out var dt))
                        reviewedAt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }

                return new ApprovalRecord { Status = status, Notes = notes, ReviewedAtUtc = reviewedAt };
            }
        }
    }
}
