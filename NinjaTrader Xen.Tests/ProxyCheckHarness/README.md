# Independent ProxyCheck harness

Runs the actual `FreeTrialService.CheckNetworkAsync` method through reflection against ProxyCheck v3. It never invokes credit eligibility, database access, email, or application startup. The capturing HTTP handler prints the response without changing the screening decision. API keys are redacted.

From the NinjaTrader repository root:

```powershell
dotnet run --project "NinjaTrader Xen.Tests/ProxyCheckHarness/ProxyCheckHarness.csproj" -- "appsettings.json" "104.255.98.77"
```

The first argument can instead point to a deployed settings file. Environment variables override that file, including `ProxyCheck__ApiKey` and `ProxyCheck__RiskThreshold`. No other settings files or user secrets are automatically loaded. A local run does not establish the live server's configuration or historical response.

Offline verification using a synthetic response (not incident evidence):

```powershell
dotnet run --project "NinjaTrader Xen.Tests/ProxyCheckHarness/ProxyCheckHarness.csproj" -- "appsettings.json" "104.255.98.77" "NinjaTrader Xen.Tests/ProxyCheckHarness/risk-100.json"
```

Expected: BLOCK despite Business type and proxy=false, because risk=100. Exit 2 means no successful HTTP response (or missing key); exit 0 only means an HTTP response was received, not that its content was valid or the IP was safe. Read the decision and body together.

The source uses `.cs.txt` with an explicit Compile item so neither the application nor the parent test project accidentally compiles the runner. The application already excludes the entire test directory from publishing.
