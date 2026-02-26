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
            return new TurnstileResult { Token = json.GetProperty("token").GetString()! };
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
                CfClearance = json.GetProperty("cookies").GetProperty("cf_clearance").GetString()!,
                UserAgent   = json.GetProperty("user_agent").GetString()!,
            };
        }

        public async Task<BalanceResult> GetBalanceAsync(CancellationToken ct = default)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/balance");
            req.Headers.Add("X-API-Key", _apiKey);

            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var raw  = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var json = JsonDocument.Parse(raw).RootElement;

            if (!resp.IsSuccessStatusCode)
                throw MakeException((int)resp.StatusCode, json);

            var result = new BalanceResult {
                Balance    = json.GetProperty("balance").GetDouble(),
                MaxThreads = json.GetProperty("max_threads").GetInt32(),
                Unlimited  = json.TryGetProperty("unlimited", out var u) && u.GetBoolean(),
            };
            if (json.TryGetProperty("allowed_types", out var arr))
            {
                var list = new List<string>();
                foreach (var item in arr.EnumerateArray()) list.Add(item.GetString() ?? "");
                result.AllowedTypes = list.ToArray();
            }
            return result;
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
                var json = JsonDocument.Parse(raw).RootElement;

                if (resp.IsSuccessStatusCode) return json;

                int status = (int)resp.StatusCode;
                bool retryable = status == 429 || status == 503;

                if (retryable && attempt < _maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct).ConfigureAwait(false);
                    continue;
                }

                throw MakeException(status, json);
            }
        }

        private static NSLSolverException MakeException(int status, JsonElement json)
        {
            var msg = json.TryGetProperty("error", out var e) ? e.GetString() ?? "" : $"HTTP {status}";
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
    }

    public class ChallengeResult
    {
        public string CfClearance { get; set; } = "";
        public string UserAgent   { get; set; } = "";
    }

    public class BalanceResult
    {
        public double   Balance      { get; set; }
        public int      MaxThreads   { get; set; }
        public bool     Unlimited    { get; set; }
        public string[] AllowedTypes { get; set; } = Array.Empty<string>();
    }
}
