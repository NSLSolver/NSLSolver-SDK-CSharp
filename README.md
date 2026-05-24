# NSLSolver C# SDK

C# client for the [NSLSolver](https://nslsolver.com) captcha API. Supports Cloudflare Turnstile, Challenge pages, and Kasada. No third-party dependencies.

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
        FpHost  = "https://fp.example.com",
        TlHost  = "https://tl.example.com",
    },
    Proxy = "http://user:pass@host:port",
});
Console.WriteLine(kasada.Ct); // x-kpsdk-ct header value
Console.WriteLine(kasada.Cd); // x-kpsdk-cd header value

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
    var result = await solver.SolveTurnstileAsync(params);
} catch (AuthenticationException) {
    // bad api key (401)
} catch (InsufficientBalanceException) {
    // add funds (402)
} catch (RateLimitException) {
    // 429, all retries exhausted
} catch (SolveException e) {
    // 400 or 503 — check e.StatusCode
} catch (NSLSolverException e) {
    Console.WriteLine($"HTTP {e.StatusCode}: {e.Message}");
}
```

## Documentation

For more information, check out the full documentation at https://docs.nslsolver.com

## License

MIT
