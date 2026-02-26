# NSLSolver C# SDK

C# client for the [NSLSolver](https://nslsolver.com) captcha API. Supports Cloudflare Turnstile and Challenge pages. No third-party dependencies.

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
Console.WriteLine(result.Token);

var challenge = await solver.SolveChallengeAsync(new ChallengeParams {
    Url   = "https://example.com/protected",
    Proxy = "http://user:pass@host:port",
});
Console.WriteLine(challenge.CfClearance);

var balance = await solver.GetBalanceAsync();
Console.WriteLine($"{balance.Balance} / {balance.MaxThreads} threads");
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

## License

MIT
