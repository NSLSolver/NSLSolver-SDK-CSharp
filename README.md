# NSLSolver C# SDK

C# client for the [NSLSolver](https://nslsolver.com) captcha API. Supports Cloudflare Turnstile, Challenge pages, Kasada, Akamai Bot Manager, and reCAPTCHA v3. No third-party dependencies.

Requires .NET 6+.

## Install

```bash
dotnet add package NSLSolver
```

## Usage

```csharp
using NSLSolver;

using var solver = new NSLSolverClient("your-api-key");

var result = await solver.SolveTurnstileAsync(new TurnstileParams {
    SiteKey = "0x4AAAAAAAB...",
    Url     = "https://example.com",
});
Console.WriteLine($"{result.Token} (cost ${result.Cost})");

var challenge = await solver.SolveChallengeAsync(new ChallengeParams {
    Url   = "https://example.com/protected",
    Proxy = "http://user:pass@host:port",
});
Console.WriteLine(challenge.CfClearance);

var kasada = await solver.SolveKasadaAsync(new KasadaParams {
    Url       = "https://example.com/api",
    UserAgent = "Mozilla/5.0 ... Chrome/131.0.0.0 ...",
    UaVersion = 131,
    KasadaConfig = new KasadaConfig {
        PJsPath = "/ips.js",
        FpHost  = "fp.example.com", // bare hostname, no scheme/path
        TlHost  = "tl.example.com", // bare hostname, no scheme/path
    },
    Proxy = "http://user:pass@host:port",
});
Console.WriteLine(kasada.Ct); // x-kpsdk-ct header value
Console.WriteLine(kasada.Cd); // x-kpsdk-cd header value

var akamai = await solver.SolveAkamaiAsync(new AkamaiParams {
    Url       = "https://example.com",
    UserAgent = "Mozilla/5.0 ... Chrome/131.0.0.0 ...", // required; replay with this same UA
    Proxy     = "http://user:pass@host:port",           // required; _abck is bound to this exit IP
});
Console.WriteLine(akamai.Abck); // _abck cookie value

var recaptcha = await solver.SolveRecaptchaV3Async(new RecaptchaV3Params {
    SiteKey    = "6Lc...",
    Url        = "https://example.com",
    Proxy      = "http://user:pass@host:port", // required
    Action     = "login",                       // optional; defaults to "verify"
    Enterprise = false,                          // set true for reCAPTCHA Enterprise site keys
});
Console.WriteLine(recaptcha.Token);

var balance = await solver.GetBalanceAsync();
Console.WriteLine($"${balance.Balance:F4}  CPM: {balance.CurrentCpm}/{balance.CpmLimit}  unlimited={balance.Unlimited}");
```

## Configuration

```csharp
using var solver = new NSLSolverClient("your-api-key", new ClientOptions {
    TimeoutSeconds = 60,
    MaxRetries     = 5,
    BaseUrl        = "https://api.nslsolver.com",
});
```

Defaults: 120s timeout, 3 retries. Implements `IDisposable` — use `using` or call `Dispose()` when done.

## Errors

All exceptions extend `NSLSolverException`. 429 and 503 are retried automatically.

```csharp
try {
    var result = await solver.SolveTurnstileAsync(new TurnstileParams {
        SiteKey = "0x4AAAAAAAB...",
        Url     = "https://example.com",
    });
} catch (BadRequestException) {
    // invalid parameters (400)
} catch (AuthenticationException) {
    // bad api key (401)
} catch (InsufficientBalanceException) {
    // add funds (402)
} catch (TypeNotAllowedException) {
    // captcha type not enabled for this key (403)
} catch (RateLimitException) {
    // 429, all retries exhausted
} catch (SolveException e) {
    // 503 — check e.StatusCode
} catch (NSLSolverException e) {
    Console.WriteLine($"HTTP {e.StatusCode}: {e.Message}");
}
```

`BadRequestException` (400), `AuthenticationException` (401),
`InsufficientBalanceException` (402), `TypeNotAllowedException` (403), and
`RateLimitException` (429) all extend `NSLSolverException` directly;
`SolveException` covers 503 (and is also thrown when a successful response
cannot be parsed). A non-JSON error body (e.g. an HTML 502/504 from an upstream
proxy) is surfaced through the matching `NSLSolverException` with the raw text
in the message — it never throws a raw `JsonException`.

## Documentation

For more information, check out the full documentation at https://docs.nslsolver.com

## License

MIT
