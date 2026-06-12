using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NSLSolver
{
    public class NSLSolverClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly int _maxRetries;
        private readonly bool _ownsHttp;

        public NSLSolverClient(string apiKey, ClientOptions? options = null)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _baseUrl = options?.BaseUrl?.TrimEnd('/') ?? "https://api.nslsolver.com";
            _maxRetries = options?.MaxRetries ?? 3;

            if (options?.HttpClient != null)
            {
                _http = options.HttpClient;
                _ownsHttp = false;
            }
            else
            {
                _http = new HttpClient { Timeout = TimeSpan.FromSeconds(options?.TimeoutSeconds ?? 120) };
                _ownsHttp = true;
            }
        }

        public async Task<TurnstileResult> SolveTurnstileAsync(TurnstileParams p, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object?> {
                ["type"]       = "turnstile",
                ["site_key"]   = p.SiteKey,
                ["url"]        = p.Url,
                ["action"]     = p.Action,
                ["cdata"]      = p.CData,
                ["proxy"]      = p.Proxy,
                ["user_agent"] = p.UserAgent,
            };
            var json = await PostAsync("/solve", body, ct).ConfigureAwait(false);
            return new TurnstileResult {
                Token = json.GetProperty("token").GetString()!,
                Cost  = ReadDouble(json, "cost"),
            };
        }

        public async Task<ChallengeResult> SolveChallengeAsync(ChallengeParams p, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object?> {
                ["type"]       = "challenge",
                ["url"]        = p.Url,
                ["proxy"]      = p.Proxy,
                ["user_agent"] = p.UserAgent,
            };
            var json = await PostAsync("/solve", body, ct).ConfigureAwait(false);
            return new ChallengeResult {
                CfClearance = json.TryGetProperty("cookies", out var cookies) && cookies.TryGetProperty("cf_clearance", out var cf)
                    ? cf.GetString() ?? ""
                    : "",
                UserAgent = json.TryGetProperty("user_agent", out var ua) ? ua.GetString() ?? "" : "",
                Token     = json.TryGetProperty("token", out var tok) && tok.ValueKind == JsonValueKind.String
                    ? tok.GetString()
                    : null,
                Cost = ReadDouble(json, "cost"),
            };
        }

        public async Task<KasadaResult> SolveKasadaAsync(KasadaParams p, CancellationToken ct = default)
        {
            var cfg = new Dictionary<string, object> {
                ["p_js_path"] = p.KasadaConfig.PJsPath,
                ["fp_host"]   = p.KasadaConfig.FpHost,
                ["tl_host"]   = p.KasadaConfig.TlHost,
            };
            if (p.KasadaConfig.CdConstant != null)
                cfg["cd_constant"] = p.KasadaConfig.CdConstant;
            var body = new Dictionary<string, object?> {
                ["type"]           = "kasada",
                ["url"]            = p.Url,
                ["user_agent"]     = p.UserAgent,
                ["ua_version"]     = p.UaVersion,
                ["kasada_config"]  = cfg,
                ["proxy"]          = p.Proxy,
            };
            var json = await PostAsync("/solve", body, ct).ConfigureAwait(false);
            var hasHeaders = json.TryGetProperty("headers", out var headers)
                && headers.ValueKind == JsonValueKind.Object;
            return new KasadaResult {
                Ct   = hasHeaders ? ReadHeader(headers, "x-kpsdk-ct") : "",
                Cd   = hasHeaders ? ReadHeader(headers, "x-kpsdk-cd") : "",
                V    = hasHeaders ? ReadHeader(headers, "x-kpsdk-v")  : "",
                H    = hasHeaders ? ReadHeader(headers, "x-kpsdk-h")  : "",
                Cost = ReadDouble(json, "cost"),
            };
        }

        /// <summary>
        /// Solve an Akamai Bot Manager challenge. All three of <c>Url</c>,
        /// <c>UserAgent</c>, and <c>Proxy</c> are required. The returned
        /// <c>_abck</c> cookie is bound to the proxy's egress IP and to the
        /// submitted UA — replay on the same proxy and user agent.
        /// </summary>
        public async Task<AkamaiResult> SolveAkamaiAsync(AkamaiParams p, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object?> {
                ["type"]       = "akamai",
                ["url"]        = p.Url,
                ["user_agent"] = p.UserAgent,
                ["proxy"]      = p.Proxy,
            };
            var json = await PostAsync("/solve", body, ct).ConfigureAwait(false);

            var cookies = new Dictionary<string, string>();
            if (json.TryGetProperty("cookies", out var cookiesEl) && cookiesEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in cookiesEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        cookies[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            return new AkamaiResult {
                Cookies = cookies,
                Cost    = ReadDouble(json, "cost"),
            };
        }

        /// <summary>
        /// Solve a reCAPTCHA v3 (incl. Enterprise) challenge. <c>SiteKey</c>,
        /// <c>Url</c>, and <c>Proxy</c> are required. <c>Action</c> defaults to
        /// <c>"verify"</c> server-side when omitted. Set <c>Enterprise</c> to
        /// <c>true</c> for reCAPTCHA Enterprise site keys.
        /// </summary>
        public async Task<RecaptchaV3Result> SolveRecaptchaV3Async(RecaptchaV3Params p, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object?> {
                ["type"]     = "recaptchav3",
                ["site_key"] = p.SiteKey,
                ["url"]      = p.Url,
                ["proxy"]    = p.Proxy,
            };
            // Only send action when set (server defaults to "verify").
            if (!string.IsNullOrEmpty(p.Action))
                body["action"] = p.Action;
            // Only send enterprise when true, and as a real JSON boolean.
            if (p.Enterprise)
                body["enterprise"] = true;
            // Only send user_agent when set.
            if (!string.IsNullOrEmpty(p.UserAgent))
                body["user_agent"] = p.UserAgent;

            var json = await PostAsync("/solve", body, ct).ConfigureAwait(false);
            return new RecaptchaV3Result {
                Token = json.GetProperty("token").GetString()!,
                // The response 'type' may be the hyphenated slug "recaptcha-v3"
                // rather than the request discriminator "recaptchav3"; surface
                // whatever the server returns instead of assuming.
                Type = json.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString()
                    : null,
                Cost = ReadDouble(json, "cost"),
            };
        }

        public async Task<BalanceResult> GetBalanceAsync(CancellationToken ct = default)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/balance");
            req.Headers.Add("X-API-Key", _apiKey);

            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var raw  = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            using var doc = TryParseJson(raw);

            if (!resp.IsSuccessStatusCode)
                throw MakeException((int)resp.StatusCode, doc?.RootElement, raw);

            if (doc == null)
                throw new NSLSolverException(
                    $"Could not parse balance response as JSON: {Truncate(raw)}",
                    (int)resp.StatusCode);

            var json = doc.RootElement;
            var maxCpm = json.TryGetProperty("max_cpm", out var mc) ? mc.GetInt32() : 0;
            var result = new BalanceResult {
                Balance    = json.TryGetProperty("balance", out var bal) && bal.ValueKind == JsonValueKind.Number
                    ? bal.GetDouble()
                    : 0.0,
                Unlimited  = json.TryGetProperty("unlimited", out var u) && u.GetBoolean(),
                MaxCpm     = maxCpm,
                CurrentCpm = json.TryGetProperty("current_cpm", out var cc) ? cc.GetInt32() : 0,
                CpmLimit   = json.TryGetProperty("cpm_limit", out var cl) ? cl.GetInt32() : maxCpm,
                UnlimitedExpiresAt = json.TryGetProperty("unlimited_expires_at", out var ue) && ue.ValueKind == JsonValueKind.String
                    ? ue.GetString()
                    : null,
            };
            if (json.TryGetProperty("allowed_types", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in arr.EnumerateArray()) list.Add(item.GetString() ?? "");
                result.AllowedTypes = list.ToArray();
            }
            return result;
        }

        private static double ReadDouble(JsonElement json, string key)
        {
            if (json.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetDouble();
            return 0.0;
        }

        private static string ReadHeader(JsonElement headers, string key)
        {
            if (headers.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "";
            return "";
        }

        private async Task<JsonElement> PostAsync(string path, Dictionary<string, object?> body, CancellationToken ct)
        {
            var filtered = new Dictionary<string, object?>();
            foreach (var kv in body)
                if (kv.Value != null) filtered[kv.Key] = kv.Value;

            var payload = JsonSerializer.Serialize(filtered);

            for (int attempt = 0; ; attempt++)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path) {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                req.Headers.Add("X-API-Key", _apiKey);

                var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                var raw  = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                int status = (int)resp.StatusCode;

                // Parse defensively: a non-JSON body (e.g. an HTML 502/504 from an
                // upstream proxy) must surface as an NSLSolverException, not a raw
                // System.Text.Json.JsonException.
                using var doc = TryParseJson(raw);

                if (resp.IsSuccessStatusCode)
                {
                    if (doc == null)
                        throw new SolveException(
                            $"Could not parse solve response as JSON: {Truncate(raw)}", status);
                    // Clone so the value outlives the disposed JsonDocument (which
                    // returns its pooled buffer on Dispose).
                    return doc.RootElement.Clone();
                }

                bool retryable = status == 429 || status == 503;

                if (retryable && attempt < _maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct).ConfigureAwait(false);
                    continue;
                }

                throw MakeException(status, doc?.RootElement, raw);
            }
        }

        /// <summary>
        /// Parse a response body into a <see cref="JsonDocument"/>, returning
        /// <c>null</c> when the body is empty or not valid JSON. The caller owns
        /// the returned document and must dispose it.
        /// </summary>
        private static JsonDocument? TryParseJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try { return JsonDocument.Parse(raw); }
            catch (JsonException) { return null; }
        }

        private static string Truncate(string s, int max = 200)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        private static NSLSolverException MakeException(int status, JsonElement? json, string raw)
        {
            string msg;
            if (json.HasValue && json.Value.ValueKind == JsonValueKind.Object
                && json.Value.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
            {
                msg = e.GetString() ?? $"HTTP {status}";
            }
            else if (!string.IsNullOrWhiteSpace(raw))
            {
                // Non-JSON error body (HTML, plain text, ...): surface the raw text
                // through the typed exception rather than crashing on deserialization.
                msg = $"HTTP {status}: {Truncate(raw)}";
            }
            else
            {
                msg = $"HTTP {status}";
            }

            return status switch {
                400 => new BadRequestException(msg),
                401 => new AuthenticationException(msg),
                402 => new InsufficientBalanceException(msg),
                403 => new TypeNotAllowedException(msg),
                429 => new RateLimitException(msg),
                503 => new SolveException(msg, 503),
                _   => new NSLSolverException(msg, status),
            };
        }

        public void Dispose() { if (_ownsHttp) _http.Dispose(); }
    }

    public class ClientOptions
    {
        public string?     BaseUrl        { get; set; }
        public int?        TimeoutSeconds { get; set; }
        public int?        MaxRetries     { get; set; }
        public HttpClient? HttpClient     { get; set; }
    }

    public class TurnstileParams
    {
        public string  SiteKey   { get; set; } = "";
        public string  Url       { get; set; } = "";
        public string? Action    { get; set; }
        public string? CData     { get; set; }
        public string? Proxy     { get; set; }
        public string? UserAgent { get; set; }
    }

    public class ChallengeParams
    {
        public string  Url       { get; set; } = "";
        public string  Proxy     { get; set; } = "";
        public string? UserAgent { get; set; }
    }

    public class TurnstileResult
    {
        public string Token { get; set; } = "";
        /// <summary>USD deducted from the account balance for this solve.</summary>
        public double Cost { get; set; }
    }

    public class ChallengeResult
    {
        public string  CfClearance { get; set; } = "";
        public string  UserAgent   { get; set; } = "";
        /// <summary>Set when the challenge page returned a Turnstile-style token instead of cookies.</summary>
        public string? Token       { get; set; }
        /// <summary>USD deducted from the account balance for this solve.</summary>
        public double  Cost        { get; set; }
    }

    public class KasadaParams
    {
        public string       Url          { get; set; } = "";
        public string       UserAgent    { get; set; } = "";
        public int          UaVersion    { get; set; }
        public KasadaConfig KasadaConfig { get; set; } = new();
        public string?      Proxy        { get; set; }
    }

    public class KasadaConfig
    {
        public string  PJsPath    { get; set; } = "";
        public string  FpHost     { get; set; } = "";
        public string  TlHost     { get; set; } = "";
        public string? CdConstant { get; set; }
    }

    public class KasadaResult
    {
        public string Ct { get; set; } = "";
        public string Cd { get; set; } = "";
        public string V  { get; set; } = "";
        public string H  { get; set; } = "";
        /// <summary>USD deducted from the account balance for this solve.</summary>
        public double Cost { get; set; }
    }

    public class AkamaiParams
    {
        public string Url       { get; set; } = "";
        /// <summary>Required. Replay the returned cookies with this same UA.</summary>
        public string UserAgent { get; set; } = "";
        /// <summary>Required. <c>_abck</c> is bound to this proxy's egress IP.</summary>
        public string Proxy     { get; set; } = "";
    }

    public class AkamaiResult
    {
        /// <summary>
        /// Cookie jar including <c>_abck</c>. Replay on the same UA + proxy/exit IP.
        /// </summary>
        public Dictionary<string, string> Cookies { get; set; } = new();
        /// <summary>USD deducted from the account balance for this solve.</summary>
        public double Cost { get; set; }

        /// <summary>Shortcut for the <c>_abck</c> cookie value.</summary>
        public string? Abck => Cookies.TryGetValue("_abck", out var v) ? v : null;
    }

    public class RecaptchaV3Params
    {
        /// <summary>Required. The site's reCAPTCHA site key.</summary>
        public string  SiteKey    { get; set; } = "";
        /// <summary>Required. The page URL where the token will be used.</summary>
        public string  Url        { get; set; } = "";
        /// <summary>Required. Proxy the solve runs through.</summary>
        public string  Proxy      { get; set; } = "";
        /// <summary>Optional. The reCAPTCHA action; defaults to <c>"verify"</c> server-side when omitted.</summary>
        public string? Action     { get; set; }
        /// <summary>Set to <c>true</c> for reCAPTCHA Enterprise site keys.</summary>
        public bool    Enterprise { get; set; }
        /// <summary>Optional. User agent to associate with the solve.</summary>
        public string? UserAgent  { get; set; }
    }

    public class RecaptchaV3Result
    {
        /// <summary>The reCAPTCHA v3 token to submit to the target site.</summary>
        public string  Token { get; set; } = "";
        /// <summary>
        /// The response type echoed by the API (the slug <c>"recaptcha-v3"</c>),
        /// or <c>null</c> when not present.
        /// </summary>
        public string? Type  { get; set; }
        /// <summary>USD deducted from the account balance for this solve.</summary>
        public double  Cost  { get; set; }
    }

    public class BalanceResult
    {
        public double   Balance      { get; set; }
        public bool     Unlimited    { get; set; }
        public string[] AllowedTypes { get; set; } = Array.Empty<string>();

        /// <summary>Per-key captchas-per-minute ceiling. 0 means uncapped.</summary>
        public int      MaxCpm       { get; set; }
        /// <summary>Tokens consumed in the rolling CPM window.</summary>
        public int      CurrentCpm   { get; set; }
        /// <summary>Mirror of <see cref="MaxCpm"/> for dashboards.</summary>
        public int      CpmLimit     { get; set; }
        /// <summary>ISO 8601 timestamp when an unlimited plan expires.</summary>
        public string?  UnlimitedExpiresAt { get; set; }
    }
}
